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
        Assert.Equal(1.0, results[0].Similarity, precision: 3);
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
