using Aveline.Api.Modules.CustomerConcierge.Services;
using Aveline.Api.Modules.Handbook.DTOs;
using Aveline.Api.Modules.Handbook.Models;
using Aveline.Api.Modules.Handbook.Repositories;
using Aveline.Api.Modules.Handbook.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Unit tests for <see cref="HandbookService"/>: the idempotent upsert, the embed-on-change rule,
/// audience/mode normalisation, and the query-embedding degradation.
///
/// <para>
/// Backed by a fake repository rather than the in-memory EF provider: the real repository's search
/// paths require PostgreSQL (pgvector + tsvector), so they are covered end to end by
/// <see cref="HandbookSearchPostgresTests"/>. What is asserted here is the service's own logic.
/// </para>
/// </summary>
public class HandbookServiceTests
{
    // ------------------------------------------------------------------ helpers

    private static SaveHandbookChunkRequest Chunk(
        string content = "The invitation code lifetime options are 24 hours, 7 days and 30 days.",
        string contentHash = "hash-1",
        string sourceKey = "web-docs/team",
        int ordinal = 1,
        string audience = "staff",
        string? anchor = null,
        string headingPath = "Invitations > Code lifetime") => new()
        {
            SourceKey = sourceKey,
            SourceKind = "web-docs",
            SourceTitle = "Team",
            SourceUrl = "/docs/team",
            HeadingPath = headingPath,
            Anchor = anchor,
            Content = content,
            ContentHash = contentHash,
            Audience = audience,
            Ordinal = ordinal,
        };

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private static (HandbookService service, FakeHandbookRepository repo, CountingEmbedding embedding)
        Build(IConfiguration? configuration = null, bool embeddingFails = false)
    {
        var repo = new FakeHandbookRepository();
        var embedding = new CountingEmbedding { Fail = embeddingFails };
        var service = new HandbookService(
            repo,
            embedding,
            configuration ?? Config(),
            NullLogger<HandbookService>.Instance);
        return (service, repo, embedding);
    }

    /// <summary>An embedding stub that records how often it was asked, and can be made to fail.</summary>
    private sealed class CountingEmbedding : IEmbeddingService
    {
        public int Calls { get; private set; }

        public bool Fail { get; init; }

        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail)
            {
                throw new InvalidOperationException("embedding provider unavailable");
            }

            var vector = new float[1536];
            vector[Math.Abs(text.GetHashCode(StringComparison.Ordinal)) % 1536] = 1f;
            return Task.FromResult(vector);
        }
    }

    /// <summary>An in-memory repository that records the calls the service makes.</summary>
    private sealed class FakeHandbookRepository : IHandbookRepository
    {
        public List<HandbookChunk> Chunks { get; } = [];

        public int AddCalls { get; private set; }

        public int UpdateCalls { get; private set; }

        public int EmbeddingWrites { get; private set; }

        public HandbookSearchQuery? LastQuery { get; private set; }

        public IReadOnlyList<HandbookSearchHit> SearchHits { get; set; } = [];

        public Task<HandbookChunk?> GetBySourceOrdinalAsync(
            string sourceKey,
            int ordinal,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Chunks.FirstOrDefault(
                c => c.SourceKey == sourceKey && c.Ordinal == ordinal));

        public Task<HandbookChunk> AddAsync(
            HandbookChunk chunk,
            CancellationToken cancellationToken = default)
        {
            AddCalls++;
            Chunks.Add(chunk);
            return Task.FromResult(chunk);
        }

        public Task<HandbookChunk> UpdateAsync(
            HandbookChunk chunk,
            CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            chunk.UpdatedAt = DateTime.UtcNow;
            return Task.FromResult(chunk);
        }

        public Task UpdateEmbeddingAsync(
            Guid chunkId,
            float[] embedding,
            CancellationToken cancellationToken = default)
        {
            EmbeddingWrites++;
            return Task.CompletedTask;
        }

        public Task<int> DeleteBySourceAsync(
            string sourceKey,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Chunks.RemoveAll(c => c.SourceKey == sourceKey));

        public Task<IReadOnlyList<HandbookSearchHit>> SearchAsync(
            HandbookSearchQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(SearchHits);
        }

        public Task<IReadOnlyList<HandbookSourceSummary>> ListSourcesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<HandbookSourceSummary>>(
                Chunks
                    .GroupBy(c => c.SourceKey)
                    .Select(g => new HandbookSourceSummary(
                        g.Key, g.First().SourceKind, g.First().SourceTitle, g.Count(), g.Max(c => c.UpdatedAt)))
                    .ToList());
    }

    // ------------------------------------------------------------------ upsert

    [Fact]
    public async Task First_upsert_adds_a_chunk_and_embeds_it_once()
    {
        var (service, repo, embedding) = Build();

        var saved = await service.UpsertAsync(Chunk());

        Assert.Equal("web-docs/team", saved.SourceKey);
        Assert.Equal(1, repo.AddCalls);
        Assert.Equal(1, repo.EmbeddingWrites);
        Assert.Equal(1, embedding.Calls);
    }

    [Fact]
    public async Task An_unchanged_chunk_is_a_true_no_op()
    {
        var (service, repo, embedding) = Build();
        var request = Chunk();
        await service.UpsertAsync(request);
        var updatedAt = repo.Chunks.Single().UpdatedAt;

        var saved = await service.UpsertAsync(request);

        Assert.Equal(1, repo.AddCalls);
        Assert.Equal(0, repo.UpdateCalls);
        Assert.Equal(1, repo.EmbeddingWrites);
        Assert.Equal(1, embedding.Calls);
        Assert.Equal(updatedAt, saved.UpdatedAt);
    }

    [Fact]
    public async Task Changed_content_re_embeds_and_replaces_the_body()
    {
        var (service, repo, embedding) = Build();
        await service.UpsertAsync(Chunk());

        await service.UpsertAsync(Chunk(content: "A different body entirely.", contentHash: "hash-2"));

        Assert.Equal(1, repo.UpdateCalls);
        Assert.Equal(2, repo.EmbeddingWrites);
        Assert.Equal(2, embedding.Calls);
        Assert.Equal("A different body entirely.", repo.Chunks.Single().Content);
    }

    [Fact]
    public async Task A_metadata_only_change_updates_metadata_without_re_embedding()
    {
        var (service, repo, embedding) = Build();
        await service.UpsertAsync(Chunk(anchor: null));

        await service.UpsertAsync(Chunk(anchor: "code-lifetime"));

        Assert.Equal(1, repo.UpdateCalls);
        Assert.Equal(1, repo.EmbeddingWrites);
        Assert.Equal(1, embedding.Calls);
        Assert.Equal("code-lifetime", repo.Chunks.Single().Anchor);
    }

    [Fact]
    public async Task An_unknown_audience_is_normalised_to_staff()
    {
        var (service, repo, _) = Build();

        var saved = await service.UpsertAsync(Chunk(audience: "everyone"));

        Assert.Equal("staff", saved.Audience);
        Assert.Equal("staff", repo.Chunks.Single().Audience);
    }

    // ------------------------------------------------------------------ search

    [Fact]
    public async Task Lexical_mode_does_not_call_the_embedding_provider()
    {
        var (service, repo, embedding) = Build();

        await service.SearchAsync(new HandbookSearchRequest { Query = "invitation code", Mode = "lexical" });

        Assert.Equal(0, embedding.Calls);
        Assert.NotNull(repo.LastQuery);
        Assert.Null(repo.LastQuery!.QueryEmbedding);
        Assert.Equal("lexical", repo.LastQuery.Mode);
    }

    [Fact]
    public async Task A_failed_query_embedding_degrades_the_hybrid_search_instead_of_failing()
    {
        var (service, repo, _) = Build(embeddingFails: true);

        var results = await service.SearchAsync(new HandbookSearchRequest { Query = "invitation code" });

        Assert.Empty(results);
        Assert.NotNull(repo.LastQuery);
        // The repository is asked for the hybrid mode with no vector, and answers from the lexical
        // leg. The request never fails because the embedding provider did.
        Assert.Equal("hybrid", repo.LastQuery!.Mode);
        Assert.Null(repo.LastQuery.QueryEmbedding);
    }

    [Fact]
    public async Task Top_k_falls_back_to_configuration_when_the_request_omits_it()
    {
        var (service, repo, _) = Build(Config(("Handbook:TopK", "9")));

        await service.SearchAsync(new HandbookSearchRequest { Query = "invitation code", TopK = 0 });

        Assert.Equal(9, repo.LastQuery!.TopK);
    }

    [Fact]
    public async Task An_unknown_mode_falls_back_to_hybrid()
    {
        var (service, repo, _) = Build();

        await service.SearchAsync(new HandbookSearchRequest { Query = "invitation code", Mode = "semantic-ish" });

        Assert.Equal("hybrid", repo.LastQuery!.Mode);
    }

    // ------------------------------------------------------------------ sources

    [Fact]
    public async Task Deleting_a_source_reports_how_many_chunks_were_removed()
    {
        var (service, _, _) = Build();
        await service.UpsertAsync(Chunk(ordinal: 1));
        await service.UpsertAsync(Chunk(ordinal: 2, contentHash: "hash-2", content: "Second chunk."));

        var removed = await service.DeleteSourceAsync("web-docs/team");

        Assert.Equal(2, removed);
    }

    [Fact]
    public async Task Listing_sources_groups_chunks_by_source()
    {
        var (service, _, _) = Build();
        await service.UpsertAsync(Chunk(ordinal: 1));
        await service.UpsertAsync(Chunk(ordinal: 2, contentHash: "hash-2", content: "Second chunk."));
        await service.UpsertAsync(Chunk(
            ordinal: 1, contentHash: "hash-3", content: "A Blossom is a unit of AI work.",
            sourceKey: "company/blossoms"));

        var sources = await service.ListSourcesAsync();

        Assert.Equal(2, sources.Count);
        Assert.Equal(2, sources.Single(s => s.SourceKey == "web-docs/team").ChunkCount);
        Assert.Equal(1, sources.Single(s => s.SourceKey == "company/blossoms").ChunkCount);
    }
}
