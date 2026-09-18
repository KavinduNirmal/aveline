using Aveline.Api.Modules.CustomerConcierge.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

/// <summary>A result of a semantic (pgvector cosine) memory search.</summary>
public sealed record CustomerMemorySearchResult(
    Guid Id,
    Guid CustomerId,
    string Content,
    string Category,
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

    /// <summary>Returns a memory by id within the org, or null.</summary>
    Task<CustomerMemory?> GetAsync(Guid orgId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns a customer's active memories, newest first.</summary>
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
    /// distance, restricted to an org + customer. Requires a relational (PostgreSQL) provider.
    /// </summary>
    Task<IReadOnlyList<CustomerMemorySearchResult>> SearchSemanticAsync(
        Guid orgId,
        Guid customerId,
        float[] queryEmbedding,
        int topK = 5,
        CancellationToken cancellationToken = default);

    /// <summary>Persists changes (bumps <see cref="CustomerMemory.UpdatedAt"/>).</summary>
    Task SaveAsync(CustomerMemory memory, CancellationToken cancellationToken = default);
}
