using Aveline.Api.Modules.Handbook.Models;

namespace Aveline.Api.Modules.Handbook.Repositories;

/// <summary>The inputs to one hybrid search.</summary>
/// <param name="Query">The raw user query, used by the lexical leg.</param>
/// <param name="QueryEmbedding">
/// The dense query vector, or <c>null</c> when it could not be produced. A null embedding runs the
/// lexical leg alone rather than failing the search.
/// </param>
/// <param name="Mode">
/// <c>hybrid</c> (default, both legs fused), <c>lexical</c> or <c>vector</c>. The single-leg modes
/// exist so retrieval quality can be measured per leg; the agent always asks for hybrid.
/// </param>
/// <param name="Audience">staff | customer | both. Always applied.</param>
/// <param name="TopK">How many fused results to return.</param>
/// <param name="CandidatePool">How many candidates each leg contributes before fusion.</param>
/// <param name="FusionK">The Reciprocal Rank Fusion constant.</param>
/// <param name="MinSimilarity">Cosine floor for the vector leg only.</param>
/// <param name="SourceKinds">Optional source-kind restriction; empty means all.</param>
public sealed record HandbookSearchQuery(
    string Query,
    float[]? QueryEmbedding,
    string Mode,
    string Audience,
    int TopK,
    int CandidatePool,
    int FusionK,
    double MinSimilarity,
    IReadOnlyList<string>? SourceKinds);

/// <summary>One fused hit, carrying both legs' ranks so retrieval quality is measurable per leg.</summary>
public sealed record HandbookSearchHit(
    Guid Id,
    string SourceKey,
    string SourceKind,
    string SourceTitle,
    string SourceUrl,
    string HeadingPath,
    string? Anchor,
    string Content,
    long? VectorRank,
    long? LexicalRank,
    double Score);

/// <summary>One indexed source with its chunk count.</summary>
public sealed record HandbookSourceSummary(
    string SourceKey,
    string SourceKind,
    string SourceTitle,
    int ChunkCount,
    DateTime UpdatedAt);

/// <summary>
/// Data access for <see cref="HandbookChunk"/> rows and their two search columns.
///
/// <para>
/// Neither the pgvector <c>embedding vector(1536)</c> column nor the generated
/// <c>SearchVector tsvector</c> is part of the EF model (ADR-017, ADR-025); both are written and
/// searched through raw SQL here. The hybrid search requires a relational (PostgreSQL + pgvector)
/// provider.
/// </para>
/// </summary>
public interface IHandbookRepository
{
    /// <summary>Returns the chunk at a source position, or null. This is the upsert key lookup.</summary>
    Task<HandbookChunk?> GetBySourceOrdinalAsync(
        string sourceKey,
        int ordinal,
        CancellationToken cancellationToken = default);

    /// <summary>Inserts a chunk (without its embedding).</summary>
    Task<HandbookChunk> AddAsync(HandbookChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to a tracked chunk and bumps <see cref="HandbookChunk.UpdatedAt"/>.</summary>
    Task<HandbookChunk> UpdateAsync(HandbookChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the pgvector embedding for a chunk via raw SQL. No-op on a non-relational provider
    /// (e.g. the in-memory test provider).
    /// </summary>
    Task UpdateEmbeddingAsync(
        Guid chunkId,
        float[] embedding,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes every chunk for a source. Returns how many rows were removed.</summary>
    Task<int> DeleteBySourceAsync(string sourceKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the requested search mode. <c>hybrid</c> fuses the dense and lexical legs with
    /// Reciprocal Rank Fusion in one statement; <c>lexical</c> and <c>vector</c> run a single leg
    /// so the eval can report per-leg recall. Requires a relational (PostgreSQL) provider.
    /// </summary>
    Task<IReadOnlyList<HandbookSearchHit>> SearchAsync(
        HandbookSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Every indexed source with its chunk count, for ops visibility.</summary>
    Task<IReadOnlyList<HandbookSourceSummary>> ListSourcesAsync(
        CancellationToken cancellationToken = default);
}
