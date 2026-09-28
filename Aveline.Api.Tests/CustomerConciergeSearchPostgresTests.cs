using System.Net;
using System.Net.Http.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// End-to-end test of the semantic memory search against a real PostgreSQL + pgvector container:
/// boots the full API with the container connection string and a stubbed embedding service,
/// then identifies a customer, saves memories (each embedded on a distinct axis), and verifies
/// <c>POST /internal/customers/memories/search</c> returns nearest neighbours in cosine order.
/// </summary>
public class CustomerConciergeSearchPostgresTests : IAsyncLifetime
{
    private const string InternalToken = "test-internal-token";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Enable pgvector and run all EF migrations against the real database.
        await using var bootstrap = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options);
        await bootstrap.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await bootstrap.Database.MigrateAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "http://localhost:0");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:InternalToken", InternalToken);
                builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
                // The media module is registered by the real host, and it refuses to mint a token
                // without a key. This suite exercises a different surface, so it configures the
                // same development default every other integration factory here uses rather than
                // leaving the host to fail at startup.
                builder.UseSetting("Media:Provider", "database");
                builder.UseSetting("Media:SigningKey", Convert.ToBase64String(new byte[32]));

                builder.ConfigureServices(services =>
                {
                    var embedding = services.Where(d => d.ServiceType == typeof(IEmbeddingService)).ToList();
                    foreach (var descriptor in embedding)
                    {
                        services.Remove(descriptor);
                    }
                    // Embed each input text deterministically on a per-content axis.
                    services.AddSingleton<IEmbeddingService>(new AxisEmbeddingService());
                });
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<HttpResponseMessage> InternalPostAsync(string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(InternalServiceAuthHandler.HeaderName, InternalToken);
        return await _client.SendAsync(request);
    }

    /// <summary>Seeds a User + Organization so customer FKs to Organizations resolve on Postgres.</summary>
    private async Task SeedOrganizationAsync(Guid orgId)
    {
        await using var ctx = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options);

        ctx.Users.Add(new Aveline.Api.Modules.Shared.Models.User
        {
            ClerkId = $"user_{Guid.NewGuid():N}",
            FirstName = "Test",
            LastName = "Owner",
            Email = "owner@aveline.test",
            Username = $"owner_{Guid.NewGuid():N}",
            OrganizationId = "org_legacy",
            PhoneNumber = "+94770000000",
            UserRole = "owner",
            OrganizationRole = "org:owner",
        });
        ctx.Organizations.Add(new Aveline.Api.Modules.Organizations.Models.Organization
        {
            Id = orgId,
            Name = "Test Boutique",
            Slug = $"boutique-{Guid.NewGuid():N}",
            OwnerUserId = ctx.Users.Local.Single().Id,
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task SemanticSearch_ReturnsNearestMemories_InCosineOrder()
    {
        var orgId = Guid.NewGuid();
        await SeedOrganizationAsync(orgId);

        var identify = await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94771234567", FullName = "Sarah" });
        Assert.Equal(HttpStatusCode.OK, identify.StatusCode);
        var profile = await identify.Content.ReadFromJsonAsync<CustomerProfileDto>();

        // Grant consent so memories can be stored.
        await InternalPostAsync($"/internal/customers/{profile!.CustomerId}/consent",
            new UpdateConsentRequest { OrganizationId = orgId, ConsentStatus = "granted" });

        // AxisEmbeddingService maps content hash -> axis, so the search query's embedding picks
        // the memory whose content hashes nearest. We pass the same content as the query so the
        // matching embedding axis is returned.
        var contents = new[] { "wedding saree", "birthday party", "office wear" };
        foreach (var content in contents)
        {
            var save = await InternalPostAsync($"/internal/customers/{profile.CustomerId}/memories",
                new SaveMemoryRequest { OrganizationId = orgId, Content = content, Category = "preference" });
            Assert.Equal(HttpStatusCode.Created, save.StatusCode);
        }

        var search = await InternalPostAsync("/internal/customers/memories/search",
            new MemorySearchRequest { OrganizationId = orgId, CustomerId = profile.CustomerId, Query = "wedding saree", TopK = 3 });
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);

        var results = await search.Content.ReadFromJsonAsync<List<MemorySearchResultDto>>();
        Assert.NotNull(results);
        Assert.Equal(3, results.Count);
        Assert.Equal("wedding saree", results[0].Content);
        // Hybrid is the default: the dense winner is rank 1 on the vector leg.
        Assert.Equal(1, results[0].VectorRank);
        Assert.Equal(1.0, results[0].Similarity!.Value, precision: 3);
    }

    [Fact]
    public async Task SemanticSearch_AppliesTheSimilarityFloor()
    {
        var orgId = Guid.NewGuid();
        await SeedOrganizationAsync(orgId);

        var identify = await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94771234567", FullName = "Sarah" });
        var profile = await identify.Content.ReadFromJsonAsync<CustomerProfileDto>();

        await InternalPostAsync($"/internal/customers/{profile!.CustomerId}/consent",
            new UpdateConsentRequest { OrganizationId = orgId, ConsentStatus = "granted" });

        // The stub embeds identical content on the same axis (similarity 1.0) and everything else
        // on a different axis (similarity 0.0), so a floor sits exactly between the two.
        foreach (var content in new[] { "wedding saree", "birthday party", "office wear" })
        {
            await InternalPostAsync($"/internal/customers/{profile.CustomerId}/memories",
                new SaveMemoryRequest { OrganizationId = orgId, Content = content, Category = "preference" });
        }

        var unfiltered = await InternalPostAsync("/internal/customers/memories/search",
            new MemorySearchRequest
            {
                OrganizationId = orgId, CustomerId = profile.CustomerId, Query = "wedding saree", TopK = 3,
            });
        var all = await unfiltered.Content.ReadFromJsonAsync<List<MemorySearchResultDto>>();
        Assert.Equal(3, all!.Count);

        var filtered = await InternalPostAsync("/internal/customers/memories/search",
            new MemorySearchRequest
            {
                OrganizationId = orgId,
                CustomerId = profile.CustomerId,
                Query = "wedding saree",
                TopK = 3,
                MinSimilarity = 0.5,
            });
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);

        var hits = await filtered.Content.ReadFromJsonAsync<List<MemorySearchResultDto>>();
        // Top-k alone always returns k rows however irrelevant; the floor is what stops an
        // unrelated note from being handed to the agent as context (gap B1).
        var hit = Assert.Single(hits!);
        Assert.Equal("wedding saree", hit.Content);
    }

    [Fact]
    public async Task LexicalSearch_ReturnsAnExactTokenHitWithNoCosine()
    {
        var orgId = Guid.NewGuid();
        await SeedOrganizationAsync(orgId);

        var identify = await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94771234567", FullName = "Sarah" });
        var profile = await identify.Content.ReadFromJsonAsync<CustomerProfileDto>();

        await InternalPostAsync($"/internal/customers/{profile!.CustomerId}/consent",
            new UpdateConsentRequest { OrganizationId = orgId, ConsentStatus = "granted" });

        await InternalPostAsync($"/internal/customers/{profile.CustomerId}/memories",
            new SaveMemoryRequest
            {
                OrganizationId = orgId, Content = "Wants a Banarasi silk saree", Category = "preference",
            });

        var search = await InternalPostAsync("/internal/customers/memories/search",
            new MemorySearchRequest
            {
                OrganizationId = orgId,
                CustomerId = profile.CustomerId,
                Query = "banarasi silk",
                Mode = "lexical",
            });
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);

        var results = await search.Content.ReadFromJsonAsync<List<MemorySearchResultDto>>();
        var hit = Assert.Single(results!);
        Assert.Equal("Wants a Banarasi silk saree", hit.Content);
        Assert.Equal(1, hit.LexicalRank);
        Assert.Null(hit.VectorRank);
        // A lexical-only hit has no cosine; reporting 0 would be a fabricated measurement.
        Assert.Null(hit.Similarity);
    }

    [Fact]
    public async Task Search_WithAnUnknownMode_IsRefusedWith400()
    {
        var orgId = Guid.NewGuid();
        await SeedOrganizationAsync(orgId);

        var identify = await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94771234567", FullName = "Sarah" });
        var profile = await identify.Content.ReadFromJsonAsync<CustomerProfileDto>();

        // Refused rather than silently defaulted to hybrid: a fused answer read as a single-leg
        // measurement is exactly the confusion the eval modes exist to remove.
        var response = await InternalPostAsync("/internal/customers/memories/search",
            new MemorySearchRequest
            {
                OrganizationId = orgId,
                CustomerId = profile!.CustomerId,
                Query = "silk",
                Mode = "bm25",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The real <c>/brief</c> payload, asserted against the shape the Python agent is written for.
    /// </summary>
    /// <remarks>
    /// This is the test the gap analysis asked for and could not run: it resolves the "are tags a
    /// list or a joined string" question from a live response rather than by reading the C# record
    /// and the Python <c>isinstance(..., list)</c> checks side by side. The agent reads
    /// <c>tags</c> only when it is a list, so a joined string made the backend's own tags disappear
    /// from every staff answer while the suite stayed green - the test double returned a list.
    /// </remarks>
    [Fact]
    public async Task Brief_ReturnsTheShapeTheAgentReads()
    {
        var orgId = Guid.NewGuid();
        await SeedOrganizationAsync(orgId);

        var identify = await InternalPostAsync("/internal/customers/identify",
            new IdentifyCustomerRequest { OrganizationId = orgId, PhoneNumber = "+94771234567", FullName = "Sarah" });
        var profile = await identify.Content.ReadFromJsonAsync<CustomerProfileDto>();

        await InternalPostAsync($"/internal/customers/{profile!.CustomerId}/consent",
            new UpdateConsentRequest { OrganizationId = orgId, ConsentStatus = "granted" });

        // A preference, a future occasion and a past one: the brief must carry the first two and
        // must not offer the third as upcoming.
        await InternalPostAsync($"/internal/customers/{profile.CustomerId}/preferences",
            new SavePreferenceRequest
            {
                OrganizationId = orgId,
                PreferenceKey = "general",
                PreferenceValue = "emerald silk",
                IsExplicit = true,
                Confidence = 0.90m,
            });

        var future = DateTime.UtcNow.AddDays(30);
        var past = DateTime.UtcNow.AddDays(-30);
        await InternalPostAsync($"/internal/customers/{profile.CustomerId}/events",
            new AddEventRequest { OrganizationId = orgId, EventType = "wedding", EventDate = future });
        await InternalPostAsync($"/internal/customers/{profile.CustomerId}/events",
            new AddEventRequest { OrganizationId = orgId, EventType = "birthday", EventDate = past });

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/internal/customers/{profile.CustomerId}/brief?organizationId={orgId}");
        request.Headers.Add(InternalServiceAuthHandler.HeaderName, InternalToken);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var brief = await response.Content.ReadFromJsonAsync<InteractionBriefDto>();
        Assert.NotNull(brief);
        Assert.Equal("Sarah", brief!.CustomerName);

        // Tags must be a list. The customer has none, so this asserts the *type*, which is the
        // whole point: an empty list and a null joined string are different things to the reader.
        Assert.NotNull(brief.Tags);

        Assert.Contains("emerald silk", brief.PreferenceSummary);

        // An event that has already happened is not an upcoming event.
        Assert.NotNull(brief.UpcomingEvents);
        Assert.Contains("wedding", brief.UpcomingEvents);
        Assert.DoesNotContain("birthday", brief.UpcomingEvents);
    }

    /// <summary>
    /// Deterministic stub: embeds text on an axis derived from a stable FNV-1a hash so cosine
    /// similarity is 1.0 for identical content and near 0 otherwise. Produces 1536-d vectors.
    /// </summary>
    private sealed class AxisEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
        {
            var axis = (ulong)Fnv1a(text) % 1536;
            var vector = new float[1536];
            vector[axis] = 1f;
            return Task.FromResult(vector);
        }

        private static uint Fnv1a(string s)
        {
            uint hash = 2166136261;
            foreach (var c in s)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }
}
