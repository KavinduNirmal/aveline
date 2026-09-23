using System.Net;
using System.Net.Http.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Aveline.Api.Modules.Handbook.DTOs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// End-to-end test of the handbook index against a real PostgreSQL + pgvector container: boots the
/// full API with the container connection string and a stubbed embedding service, seeds chunks
/// through <c>POST /internal/handbook/chunks</c>, then exercises the hybrid, lexical and vector
/// search modes plus the audience / active filters and the source management endpoints.
///
/// <para>
/// The embedding stub maps exact strings to a vector axis, so the dense ordering is controlled by
/// the fixture instead of by a hash. That is what makes the fusion assertion deterministic:
/// <c>chunkLexical</c> is only second on the dense leg but first on the lexical leg, and the test
/// asserts it outranks the dense winner after fusion. A single-leg search could not produce that
/// ordering, which is the whole reason the hybrid exists (ADR-025).
/// </para>
/// </summary>
public class HandbookSearchPostgresTests : IAsyncLifetime
{
    private const string InternalToken = "test-internal-token";

    private const string Query = "invitation code lifetime";

    private const string LexicalChunkContent =
        "The invitation code lifetime options are 24 hours, 7 days and 30 days.";

    private const string DenseChunkContent =
        "A colleague can join your boutique with an invitation link.";

    private const string OtherChunkContent =
        "A Blossom is the unit of AI work and is not money.";

    private const string CustomerChunkContent =
        "Internal: how to change the plan from the billing section.";

    /// <summary>
    /// Exact-text to vector map. The dense ordering is deliberately unique (1.0, 0.8, 0.0) rather
    /// than a set of orthogonal axis vectors, because orthogonal vectors tie and a tied ORDER BY
    /// would make the fusion assertion flaky. Axis 1 is the query's axis, so the dense winner is a
    /// property of the fixture.
    /// </summary>
    private static readonly Dictionary<string, float[]> VectorMap = new(StringComparer.Ordinal)
    {
        [Query] = Vector((1, 1f)),
        [DenseChunkContent] = Vector((1, 1f)),
        // Second on the dense leg (0.8) but the only lexical match: the fusion case.
        [LexicalChunkContent] = Vector((1, 0.8f), (2, 0.6f)),
        [OtherChunkContent] = Vector((3, 1f)),
        [CustomerChunkContent] = Vector((4, 1f)),
    };

    private static float[] Vector(params (int Axis, float Weight)[] components)
    {
        var vector = new float[1536];
        foreach (var (axis, weight) in components)
        {
            vector[axis] = weight;
        }

        return vector;
    }

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

        await using (var bootstrap = NewContext())
        {
            await bootstrap.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
            await bootstrap.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "http://localhost:0");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:InternalToken", InternalToken);
                builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
                // The media module validates its options at startup; these are the same
                // provider-neutral values the other WebApplicationFactory tests supply.
                builder.UseSetting("Media:PublicBaseUrl", "https://media.aveline.test");
                builder.UseSetting("Media:SigningKey", Convert.ToBase64String(new byte[32]));

                builder.ConfigureServices(services =>
                {
                    foreach (var descriptor in services
                                 .Where(d => d.ServiceType == typeof(IEmbeddingService))
                                 .ToList())
                    {
                        services.Remove(descriptor);
                    }

                    services.AddSingleton<IEmbeddingService>(new FixedAxisEmbeddingService(VectorMap));
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

    private AppDbContext NewContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    private async Task<HttpResponseMessage> InternalPostAsync(string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(InternalServiceAuthHandler.HeaderName, InternalToken);
        return await _client.SendAsync(request);
    }

    private static SaveHandbookChunkRequest Chunk(
        string sourceKey,
        string sourceKind,
        string sourceTitle,
        string sourceUrl,
        string headingPath,
        string content,
        string audience = "staff",
        int ordinal = 1) => new()
        {
            SourceKey = sourceKey,
            SourceKind = sourceKind,
            SourceTitle = sourceTitle,
            SourceUrl = sourceUrl,
            HeadingPath = headingPath,
            Content = content,
            ContentHash = $"hash-{sourceKey}-{ordinal}",
            Audience = audience,
            Ordinal = ordinal,
        };

    private async Task SeedAsync()
    {
        var chunks = new[]
        {
            Chunk("web-docs/team", "web-docs", "Team", "/docs/team",
                "Invitations > Code lifetime", LexicalChunkContent),
            Chunk("web-docs/joining-a-boutique", "web-docs", "Joining a Boutique",
                "/docs/joining-a-boutique", "Join with an invitation link", DenseChunkContent),
            Chunk("company/blossoms", "company", "Blossoms", "/docs/usage",
                "What a Blossom is", OtherChunkContent),
            Chunk("company/plans-and-pricing", "company", "Plans and Pricing", "/contact",
                "Change your plan", CustomerChunkContent, audience: "customer"),
        };

        foreach (var chunk in chunks)
        {
            var response = await InternalPostAsync("/internal/handbook/chunks", chunk);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private async Task<List<HandbookSearchResultDto>> SearchAsync(
        string query,
        string mode = "hybrid",
        string audience = "staff",
        int topK = 5)
    {
        var response = await InternalPostAsync("/internal/handbook/search", new HandbookSearchRequest
        {
            Query = query,
            Mode = mode,
            Audience = audience,
            TopK = topK,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<List<HandbookSearchResultDto>>() ?? [];
    }

    // ------------------------------------------------------------------ indexes

    [Fact]
    public async Task The_migration_creates_both_search_columns_and_indexes()
    {
        await using var context = NewContext();

        var columns = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'HandbookChunk' AND column_name IN ('embedding', 'SearchVector')
                ORDER BY column_name
                """)
            .ToListAsync();
        Assert.Equal(new[] { "SearchVector", "embedding" }, columns);

        var indexes = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT indexname AS "Value"
                FROM pg_indexes
                WHERE tablename = 'HandbookChunk'
                  AND indexname IN ('IX_HandbookChunk_Embedding', 'IX_HandbookChunk_SearchVector')
                ORDER BY indexname
                """)
            .ToListAsync();
        Assert.Equal(new[] { "IX_HandbookChunk_Embedding", "IX_HandbookChunk_SearchVector" }, indexes);
    }

    // ------------------------------------------------------------------ modes

    [Fact]
    public async Task Vector_mode_ranks_by_cosine_similarity()
    {
        await SeedAsync();

        var results = await SearchAsync(Query, mode: "vector");

        Assert.Equal("web-docs/joining-a-boutique", results[0].SourceKey);
        Assert.Equal(1, results[0].VectorRank);
        Assert.Equal(1.0, results[0].Score, precision: 3);
        Assert.All(results, r => Assert.Null(r.LexicalRank));
    }

    [Fact]
    public async Task Lexical_mode_answers_an_exact_label_query()
    {
        await SeedAsync();

        var results = await SearchAsync(Query, mode: "lexical");

        // Only the chunk that actually contains "invitation", "code" and "lifetime" matches; the
        // dense winner does not, which is exactly why a keyword-shaped query needs this leg.
        var hit = Assert.Single(results);
        Assert.Equal("web-docs/team", hit.SourceKey);
        Assert.Equal(1, hit.LexicalRank);
        Assert.Null(hit.VectorRank);
    }

    [Fact]
    public async Task Hybrid_fusion_lifts_a_lexical_hit_above_the_dense_winner()
    {
        await SeedAsync();

        var results = await SearchAsync(Query);

        Assert.Equal(3, results.Count);
        // Second on the dense leg, first on the lexical leg: only fusion can put it first.
        Assert.Equal("web-docs/team", results[0].SourceKey);
        Assert.Equal(1, results[0].LexicalRank);
        Assert.Equal(2, results[0].VectorRank);

        // The dense winner carries its vector rank and no lexical rank, and still places second.
        Assert.Equal("web-docs/joining-a-boutique", results[1].SourceKey);
        Assert.Equal(1, results[1].VectorRank);
        Assert.Null(results[1].LexicalRank);

        Assert.True(results[0].Score > results[1].Score);
    }

    // ------------------------------------------------------------------ filters

    [Fact]
    public async Task The_audience_filter_keeps_a_customer_chunk_out_of_a_staff_search()
    {
        await SeedAsync();

        var staff = await SearchAsync("change the plan", mode: "lexical", audience: "staff");
        Assert.DoesNotContain(staff, r => r.SourceKey == "company/plans-and-pricing");

        var customer = await SearchAsync("change the plan", mode: "lexical", audience: "customer");
        Assert.Contains(customer, r => r.SourceKey == "company/plans-and-pricing");
    }

    [Fact]
    public async Task An_inactive_chunk_is_not_retrieved()
    {
        await SeedAsync();

        await using (var context = NewContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE \"HandbookChunk\" SET \"IsActive\" = false WHERE \"SourceKey\" = 'company/blossoms'");
        }

        var results = await SearchAsync(Query, mode: "vector");

        Assert.DoesNotContain(results, r => r.SourceKey == "company/blossoms");
    }

    [Fact]
    public async Task Source_kind_restriction_is_applied()
    {
        await SeedAsync();

        var response = await InternalPostAsync("/internal/handbook/search", new HandbookSearchRequest
        {
            Query = Query,
            Audience = "staff",
            SourceKinds = ["company"],
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<HandbookSearchResultDto>>() ?? [];

        // The lexical and dense winners are both web-docs; "company" has only the Blossom chunk,
        // which matches neither leg for this query.
        Assert.DoesNotContain(results, r => r.SourceKind == "web-docs");
    }

    // ------------------------------------------------------------------ sources

    [Fact]
    public async Task Sources_are_listed_with_their_chunk_counts()
    {
        await SeedAsync();

        var response = await _client.SendAsync(new HttpRequestMessage(
            HttpMethod.Get, "/internal/handbook/sources")
        {
            Headers = { { InternalServiceAuthHandler.HeaderName, InternalToken } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sources = await response.Content.ReadFromJsonAsync<List<HandbookSourceSummaryDto>>() ?? [];

        Assert.Equal(4, sources.Count);
        Assert.Equal(1, sources.Single(s => s.SourceKey == "web-docs/team").ChunkCount);
    }

    [Fact]
    public async Task Deleting_a_source_removes_its_chunks()
    {
        await SeedAsync();

        var delete = new HttpRequestMessage(
            HttpMethod.Delete, "/internal/handbook/sources/web-docs/team")
        {
            Headers = { { InternalServiceAuthHandler.HeaderName, InternalToken } },
        };
        var response = await _client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await SearchAsync(Query, mode: "lexical");
        Assert.DoesNotContain(results, r => r.SourceKey == "web-docs/team");
    }

    [Fact]
    public async Task Seeding_through_the_endpoint_is_idempotent()
    {
        await SeedAsync();
        await SeedAsync();

        await using var context = NewContext();
        var count = await context.HandbookChunks.CountAsync();

        Assert.Equal(4, count);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Deterministic stub: the exact input text selects a precomputed 1536-d vector, so the dense
    /// ordering is a property of the fixture rather than of a hash. 1536 dimensions, like the real
    /// provider (ADR-017).
    /// </summary>
    private sealed class FixedAxisEmbeddingService(Dictionary<string, float[]> vectorByText) : IEmbeddingService
    {
        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
        {
            var vector = vectorByText.TryGetValue(text.Trim(), out var known)
                ? known
                : new float[1536];
            return Task.FromResult(vector);
        }
    }
}
