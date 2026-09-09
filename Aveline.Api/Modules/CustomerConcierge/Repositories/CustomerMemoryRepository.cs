using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

public class CustomerMemoryRepository : ICustomerMemoryRepository
{
    private readonly AppDbContext _context;

    public CustomerMemoryRepository(AppDbContext context) => _context = context;

    public async Task<CustomerMemory> AddAsync(CustomerMemory memory, CancellationToken cancellationToken = default)
    {
        _context.CustomerMemories.Add(memory);
        await _context.SaveChangesAsync(cancellationToken);
        return memory;
    }

    public async Task<CustomerMemory?> GetAsync(Guid orgId, Guid id, CancellationToken cancellationToken = default)
        => await _context.CustomerMemories
            .FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CustomerMemory>> ListByCustomerAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default)
        => await _context.CustomerMemories
            .Where(m => m.OrganizationId == orgId && m.CustomerId == customerId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task UpdateEmbeddingAsync(
        Guid orgId,
        Guid memoryId,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        // Embeddings only exist on PostgreSQL (pgvector). No-op elsewhere (e.g. the in-memory
        // test provider) so callers can persist memory rows without a live vector column.
        if (!_context.Database.IsRelational())
        {
            return;
        }

        // Serialize the vector in pgvector literal form: [a,b,c].
        var literal = "[" + string.Join(",", embedding) + "]";
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE \"Customer_Memory\" SET embedding = CAST({0} AS vector) " +
            "WHERE \"OrganizationId\" = {1} AND \"Id\" = {2}",
            new object[] { literal, orgId, memoryId },
            cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerMemorySearchResult>> SearchSemanticAsync(
        Guid orgId,
        Guid customerId,
        float[] queryEmbedding,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsRelational())
        {
            throw new InvalidOperationException(
                "Semantic memory search requires a relational (PostgreSQL + pgvector) provider.");
        }

        var literal = "[" + string.Join(",", queryEmbedding) + "]";
        var sql = """
                  SELECT "Id", "CustomerId", "Content", "Category", "Confidence", "IsExplicit",
                         1 - (embedding <=> CAST({0} AS vector)) AS "Similarity"
                  FROM "Customer_Memory"
                  WHERE "OrganizationId" = {1}
                    AND "CustomerId" = {2}
                    AND "DeletedAt" IS NULL
                    AND embedding IS NOT NULL
                  ORDER BY embedding <=> CAST({0} AS vector)
                  LIMIT {3}
                  """;

        var rows = await _context.Database
            .SqlQueryRaw<MemorySearchRow>(sql, literal, orgId, customerId, topK)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new CustomerMemorySearchResult(
                r.Id, r.CustomerId, r.Content, r.Category, r.Confidence, r.IsExplicit, r.Similarity))
            .ToList();
    }

    public async Task SaveAsync(CustomerMemory memory, CancellationToken cancellationToken = default)
    {
        memory.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    // Private row projection matching the raw SQL select (public for SqlQueryRaw metadata).
    public sealed class MemorySearchRow
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Category { get; set; } = "fact";
        public decimal Confidence { get; set; }
        public bool IsExplicit { get; set; }
        public double Similarity { get; set; }
    }
}
