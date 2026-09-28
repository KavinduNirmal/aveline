using Aveline.Api.Modules.CustomerConcierge.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

/// <summary>The inputs to one memory search.</summary>
/// <param name="Query">The raw query text, used by the lexical leg.</param>
/// <param name="QueryEmbedding">
/// The dense query vector, or <c>null</c> when it could not be produced. A null embedding runs the
/// lexical leg alone rather than failing the search.
/// </param>
/// <param name="Mode">
/// <c>hybrid</c> (default, both legs fused), <c>lexical</c> or <c>vector</c>. The single-leg modes
/// exist so retrieval quality can be measured per leg; the agent always asks for hybrid.
/// </param>
/// <param name="TopK">How many fused results to return.</param>
/// <param name="MinSimilarity">Cosine floor for the vector leg only.</param>
public sealed record CustomerMemorySearchQuery(
    string Query,
    float[]? QueryEmbedding,
    string Mode,
    Guid OrganizationId,
    Guid CustomerId,
    int TopK,
    double MinSimilarity);

/// <summary>
/// One memory search hit. Both legs' ranks are exposed, not just the fused score, so the retrieval
/// eval can report vector, lexical and hybrid recall separately (ADR-025).
/// </summary>
/// <param name="Similarity">
/// The dense leg's cosine similarity, or <c>null</c> when only the lexical leg found the row: a
/// lexical-only hit has no cosine, and reporting <c>0</c> would be a fabricated measurement.
/// </param>
public sealed record CustomerMemorySearchResult(
    Guid Id,
    Guid CustomerId,
    string Content,
    string Category,
    string Source,
    decimal Confidence,
    bool IsExplicit,
    double? Similarity,
    long? VectorRank,
    long? LexicalRank,
    double Score);

/// <summary>
/// Data access for <see cref="CustomerMemory"/> rows and their pgvector embeddings.
/// The <c>embedding vector(1536)</c> column and the generated <c>SearchVector tsvector</c> column
/// are not part of the EF model (see ADR-017); both are written and searched through raw SQL.
/// Tenant-scoped and soft-delete aware.
/// </summary>
public interface ICustomerMemoryRepository
{
    /// <summary>Creates a memory row (without its embedding).</summary>
    Task<CustomerMemory> AddAsync(CustomerMemory memory, CancellationToken cancellationToken = default);

    /// <summary>Returns a memory by id within the org, or null. Soft-deleted rows are invisible.</summary>
    Task<CustomerMemory?> GetAsync(Guid orgId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the customer's live note carrying <paramref name="contentKey"/>, or null (gap A3).
    /// </summary>
    /// <remarks>
    /// The write path asks this before inserting so a restatement is answered with the row that
    /// already exists rather than becoming a second one. The unique index behind the column is what
    /// makes the answer authoritative when two writers race; this read is the ordinary path.
    /// </remarks>
    Task<CustomerMemory?> GetByContentKeyAsync(
        Guid orgId,
        Guid customerId,
        string contentKey,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a customer's live memories, newest first.</summary>
    Task<IReadOnlyList<CustomerMemory>> ListByCustomerAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the pgvector embedding for a memory (raw SQL). No-op on a non-relational provider
    /// (e.g. the in-memory test provider).
    /// </summary>
    Task UpdateEmbeddingAsync(
        Guid orgId,
        Guid memoryId,
        float[] embedding,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the requested search mode over one customer's live memories. <c>hybrid</c> fuses the
    /// dense (pgvector cosine) and lexical (PostgreSQL full-text) legs with Reciprocal Rank Fusion
    /// in one statement; <c>lexical</c> and <c>vector</c> run a single leg so the eval can report
    /// per-leg recall. Requires a relational (PostgreSQL + pgvector) provider.
    /// </summary>
    /// <remarks>
    /// The organisation + customer scope, the <c>DeletedAt IS NULL</c> filter and the
    /// <c>ExpiresAt</c> filter are applied to every leg and in every mode. The
    /// <see cref="CustomerMemorySearchQuery.MinSimilarity"/> floor bounds the dense leg only: a
    /// lexical hit has no cosine score to floor, and a post-fusion floor would delete exactly the
    /// lexical-only hits the hybrid exists to surface.
    /// </remarks>
    Task<IReadOnlyList<CustomerMemorySearchResult>> SearchAsync(
        CustomerMemorySearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Persists changes (bumps <see cref="CustomerMemory.UpdatedAt"/>).</summary>
    Task SaveAsync(CustomerMemory memory, CancellationToken cancellationToken = default);
}
