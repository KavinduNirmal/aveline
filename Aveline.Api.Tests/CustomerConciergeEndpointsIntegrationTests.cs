using System.Net;
using System.Net.Http.Json;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Tests;

/// <summary>
/// Integration tests for the internal Customer Concierge endpoints (Issue #3), running the real
/// API against the in-memory EF provider with a stubbed embedding service. Covers authentication
/// (401) and the happy paths that do not require a live pgvector column (semantic search is
/// exercised separately against a real PostgreSQL container).
/// </summary>
public class CustomerConciergeEndpointsIntegrationTests : IAsyncLifetime
{
    private const string InternalToken = "test-internal-token";
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "http://localhost:0");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:InternalToken", InternalToken);

                // Stub the embedding provider so endpoints never make a real call.
                builder.ConfigureServices(services =>
                {
                    var descriptors = services
                        .Where(d => d.ServiceType == typeof(IEmbeddingService))
                        .ToList();
                    foreach (var descriptor in descriptors)
                    {
                        services.Remove(descriptor);
                    }
                    services.AddSingleton<IEmbeddingService>(new StubEmbeddingService());
                });
            });

        _client = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<HttpResponseMessage> InternalPostAsync(string path, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add(InternalServiceAuthHandler.HeaderName, InternalToken);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> InternalGetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(InternalServiceAuthHandler.HeaderName, InternalToken);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Identify_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = Guid.NewGuid(), PhoneNumber = "+94771234567" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Identify_WithValidToken_CreatesProfile()
    {
        var orgId = Guid.NewGuid();
        var response = await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94771234567", FullName = "Sarah" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<CustomerProfileDto>();
        Assert.NotNull(profile);
        Assert.Equal("new", profile.Status);
        Assert.Equal("pending", profile.ConsentStatus);
    }

    [Fact]
    public async Task Lookup_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/internal/customers/lookup",
            new CustomerLookupRequest { OrganizationId = Guid.NewGuid(), Name = "Samantha" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_ByPhone_ReturnsExactMatch()
    {
        var orgId = Guid.NewGuid();
        var identify = await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94771234567", FullName = "Samantha Arias" });
        await identify.Content.ReadFromJsonAsync<CustomerProfileDto>();

        var response = await InternalPostAsync("/internal/customers/lookup",
            new CustomerLookupRequest { OrganizationId = orgId, PhoneNumber = "+94771234567" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lookup = await response.Content.ReadFromJsonAsync<CustomerLookupResponse>();
        Assert.NotNull(lookup);
        Assert.True(lookup!.IsExact);
        Assert.Single(lookup.Matches);
        Assert.Equal("Samantha Arias", lookup.Matches[0].FullName);
    }

    [Fact]
    public async Task Lookup_ByName_MultipleMatches_IsNotExact()
    {
        var orgId = Guid.NewGuid();
        await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94770000001", FullName = "Samantha Arias" });
        await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94770000002", FullName = "Samantha Ranaweera" });

        var response = await InternalPostAsync("/internal/customers/lookup",
            new CustomerLookupRequest { OrganizationId = orgId, Name = "Samantha" });

        var lookup = await response.Content.ReadFromJsonAsync<CustomerLookupResponse>();
        Assert.NotNull(lookup);
        Assert.False(lookup!.IsExact);
        Assert.Equal(2, lookup.Total);
    }

    [Fact]
    public async Task Lookup_NoMatch_ReturnsEmpty()
    {
        var orgId = Guid.NewGuid();
        var response = await InternalPostAsync("/internal/customers/lookup",
            new CustomerLookupRequest { OrganizationId = orgId, Name = "Zara Nobody" });

        var lookup = await response.Content.ReadFromJsonAsync<CustomerLookupResponse>();
        Assert.NotNull(lookup);
        Assert.Empty(lookup!.Matches);
        Assert.False(lookup.IsExact);
    }

    [Fact]
    public async Task Lookup_NoCriteria_ReturnsBadRequest()
    {
        var response = await InternalPostAsync("/internal/customers/lookup",
            new CustomerLookupRequest { OrganizationId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SaveMemory_WithValidToken_ReturnsCreated()
    {
        var orgId = Guid.NewGuid();
        var identify = await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94771234567" });
        var profile = await identify.Content.ReadFromJsonAsync<CustomerProfileDto>();

        var response = await InternalPostAsync($"/internal/customers/{profile!.CustomerId}/memories",
            new SaveMemoryRequest { OrganizationId = orgId, Content = "Prefers silk", Category = "preference" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var memory = await response.Content.ReadFromJsonAsync<CustomerMemoryDto>();
        Assert.NotNull(memory);
        Assert.Equal("Prefers silk", memory.Content);
    }

    [Fact]
    public async Task GenerateBrief_WithValidToken_ReturnsBrief()
    {
        var orgId = Guid.NewGuid();
        var identify = await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94771234567", FullName = "Sarah Perera" });
        var profile = await identify.Content.ReadFromJsonAsync<CustomerProfileDto>();

        var response = await InternalGetAsync($"/internal/customers/{profile!.CustomerId}/brief?organizationId={orgId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var brief = await response.Content.ReadFromJsonAsync<InteractionBriefDto>();
        Assert.NotNull(brief);
        Assert.Equal("Sarah Perera", brief.CustomerName);
    }

    [Fact]
    public async Task Consent_UpdateAndRead_RoundTrips()
    {
        var orgId = Guid.NewGuid();
        var identify = await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94771234567" });
        var profile = await identify.Content.ReadFromJsonAsync<CustomerProfileDto>();

        var update = await InternalPostAsync($"/internal/customers/{profile!.CustomerId}/consent",
            new UpdateConsentRequest { OrganizationId = orgId, ConsentStatus = "granted" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var get = await InternalGetAsync($"/internal/customers/{profile.CustomerId}/consent?organizationId={orgId}");
        var consent = await get.Content.ReadFromJsonAsync<CustomerConsentDto>();
        Assert.Equal("granted", consent!.ConsentStatus);
    }

    [Fact]
    public async Task Events_AddAndList_RoundTrips()
    {
        var orgId = Guid.NewGuid();
        var identify = await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94771234567" });
        var profile = await identify.Content.ReadFromJsonAsync<CustomerProfileDto>();

        var add = await InternalPostAsync($"/internal/customers/{profile!.CustomerId}/events",
            new AddEventRequest { OrganizationId = orgId, EventType = "wedding", EventDate = new DateTime(2026, 12, 1) });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);

        var list = await InternalGetAsync($"/internal/customers/{profile.CustomerId}/events?organizationId={orgId}");
        var events = await list.Content.ReadFromJsonAsync<List<CustomerEventDto>>();
        Assert.Single(events!);
        Assert.Equal("wedding", events[0].EventType);
    }

    [Fact]
    public async Task RecordInteraction_WithValidToken_ReturnsCreated()
    {
        var orgId = Guid.NewGuid();
        var identify = await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94771234567" });
        var profile = await identify.Content.ReadFromJsonAsync<CustomerProfileDto>();

        var response = await InternalPostAsync($"/internal/customers/{profile!.CustomerId}/interactions",
            new RecordInteractionRequest
            {
                OrganizationId = orgId,
                Channel = "whatsapp",
                Direction = "inbound",
                MessageContent = "I need a blue saree",
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var interaction = await response.Content.ReadFromJsonAsync<CustomerInteractionDto>();
        Assert.Equal("whatsapp", interaction!.Channel);
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        private static readonly float[] Vector = Enumerable.Range(0, 1536).Select(i => (float)i).ToArray();

        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(Vector);
    }
}
