using Aveline.Api.Modules.CustomerConcierge.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

/// <summary>A result of a semantic (pgvector cosine) memory search.</summary>
public sealed record CustomerMemorySearchResult(
    Guid Id,
    Guid CustomerId,
    string Content,
    string Category,
    string Source,
    decimal Confidence,
    bool IsExplicit,
    double Similarity);

/// <summary>
/// Data access for <see cref="CustomerMemory"/> rows and their pgvector embeddings.
/// The <c>embedding vector(1536)</c> column is not part of the EF model (see ADR-017); it is
/// written and searched through raw SQL. Tenant-scoped and soft-delete aware.
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
    /// Returns the memories nearest to <paramref name="queryEmbedding"/> by pgvector cosine
    /// distance, restricted to an org + customer, and to those at or above
    /// <paramref name="minSimilarity"/>. Requires a relational (PostgreSQL) provider.
    /// </summary>
    /// <remarks>
    /// A floor is a filter, not a ranking: top-k always returns k rows however irrelevant, and a
    /// customer whose notes say nothing about the current message must retrieve nothing rather than
    /// the five least-unrelated notes on file (gap B1).
    /// </remarks>
    Task<IReadOnlyList<CustomerMemorySearchResult>> SearchSemanticAsync(
        Guid orgId,
        Guid customerId,
        float[] queryEmbedding,
        int topK = 5,
        double minSimilarity = 0.0,
        CancellationToken cancellationToken = default);

    /// <summary>Persists changes (bumps <see cref="CustomerMemory.UpdatedAt"/>).</summary>
    Task SaveAsync(CustomerMemory memory, CancellationToken cancellationToken = default);
}
