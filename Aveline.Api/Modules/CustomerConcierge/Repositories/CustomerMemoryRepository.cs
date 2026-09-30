using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Common;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

public class CustomerMemoryRepository : ICustomerMemoryRepository
{
    private readonly AppDbContext _context;

    public CustomerMemoryRepository(AppDbContext context) => _context = context;

    public async Task<CustomerMemory> AddAsync(CustomerMemory memory, CancellationToken cancellationToken = default)
    {
        EnsureContentKey(memory);
        _context.CustomerMemories.Add(memory);
        await _context.SaveChangesAsync(cancellationToken);
        return memory;
    }

    /// <summary>
    /// Derives the statement key from the content when a caller has not set it.
    /// </summary>
    /// <remarks>
    /// The key is derived data, so the store owns it rather than trusting each caller to remember
    /// it. Without this, a writer that bypasses <c>CustomerMemoryService</c> would insert the
    /// column default (an empty string) and the unique index would then refuse a second note whose
    /// content differs but whose key is also empty - a constraint failure with no relationship to
    /// what the caller actually wrote. The service still sets the key itself, because it must read
    /// the key *before* inserting to answer a restatement with the existing row.
    /// </remarks>
    private static void EnsureContentKey(CustomerMemory memory)
    {
        if (string.IsNullOrEmpty(memory.ContentKey))
        {
            memory.ContentKey = MemoryContentKey.From(memory.Content);
        }
    }

    public async Task<CustomerMemory?> GetAsync(Guid orgId, Guid id, CancellationToken cancellationToken = default)
        => await _context.CustomerMemories
            .FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.Id == id, cancellationToken);

    public async Task<CustomerMemory?> GetByContentKeyAsync(
        Guid orgId,
        Guid customerId,
        string contentKey,
        CancellationToken cancellationToken = default)
        => await _context.CustomerMemories
            .FirstOrDefaultAsync(
                m => m.OrganizationId == orgId
                     && m.CustomerId == customerId
                     && m.ContentKey == contentKey,
                cancellationToken);

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
            "UPDATE \"CustomerMemory\" SET embedding = CAST({0} AS vector) " +
            "WHERE \"OrganizationId\" = {1} AND \"Id\" = {2}",
            new object[] { literal, orgId, memoryId },
            cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerMemorySearchResult>> SearchAsync(
        CustomerMemorySearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsRelational())
        {
            throw new InvalidOperationException(
                "Memory search requires a relational (PostgreSQL + pgvector) provider.");
        }

        // Normalise here as well as in the service: a repository caller that names a mode this
        // store cannot run must be told, not silently given hybrid results. An absent mode is
        // hybrid (ADR-025).
        var mode = MemorySearchModes.Normalise(query.Mode);

        var rows = mode switch
        {
            MemorySearchModes.Lexical => await SearchLexicalAsync(query, cancellationToken),
            MemorySearchModes.Vector => query.QueryEmbedding is null
                // An explicit vector request with no vector cannot answer; returning nothing is
                // honest, where silently answering lexically would misreport what was measured.
                ? []
                : await SearchVectorAsync(query, cancellationToken),
            _ => query.QueryEmbedding is null
                // The embedding provider was unavailable. Hybrid degrades to the lexical leg rather
                // than failing; `VectorRank` is NULL for every row. This is what makes a provider
                // outage a degradation instead of an outage.
                ? await SearchLexicalAsync(query, cancellationToken)
                : await SearchHybridAsync(query, cancellationToken),
        };

        return rows;
    }

    private async Task<IReadOnlyList<CustomerMemorySearchResult>> SearchHybridAsync(
        CustomerMemorySearchQuery query,
        CancellationToken cancellationToken)
    {
        var (topK, pool, fusionK) = Normalise(query);
        var literal = VectorLiteral(query.QueryEmbedding!);

        return await RunAsync(
            HybridSql,
            [query.Query, literal, query.OrganizationId, query.CustomerId, pool, fusionK, topK, query.MinSimilarity],
            cancellationToken);
    }

    private async Task<IReadOnlyList<CustomerMemorySearchResult>> SearchLexicalAsync(
        CustomerMemorySearchQuery query,
        CancellationToken cancellationToken)
    {
        var (topK, pool, fusionK) = Normalise(query);

        return await RunAsync(
            LexicalOnlySql,
            [query.Query, query.OrganizationId, query.CustomerId, pool, topK, fusionK],
            cancellationToken);
    }

    private async Task<IReadOnlyList<CustomerMemorySearchResult>> SearchVectorAsync(
        CustomerMemorySearchQuery query,
        CancellationToken cancellationToken)
    {
        var (topK, pool, _) = Normalise(query);
        var literal = VectorLiteral(query.QueryEmbedding!);

        return await RunAsync(
            VectorOnlySql,
            [literal, query.OrganizationId, query.CustomerId, pool, topK, query.MinSimilarity],
            cancellationToken);
    }

    private async Task<IReadOnlyList<CustomerMemorySearchResult>> RunAsync(
        string sql,
        object[] args,
        CancellationToken cancellationToken)
    {
        var rows = await _context.Database
            .SqlQueryRaw<MemorySearchRow>(sql, args)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new CustomerMemorySearchResult(
                r.Id, r.CustomerId, r.Content, r.Category, r.Source, r.Confidence, r.IsExplicit,
                r.Similarity, r.VectorRank, r.LexicalRank, r.Score))
            .ToList();
    }

    private static (int TopK, int Pool, int FusionK) Normalise(CustomerMemorySearchQuery query)
    {
        var topK = Math.Max(query.TopK, 1);
        return (topK, Math.Max(topK, DefaultCandidatePool), DefaultFusionK);
    }

    private static string VectorLiteral(float[] embedding)
        => "[" + string.Join(",", embedding) + "]";

    /// <summary>
    /// The fused statement: one dense leg (pgvector cosine) and one lexical leg (PostgreSQL
    /// full-text) over the same org + customer + live + unexpired filter, fused with Reciprocal Rank
    /// Fusion. Mirrors the handbook's <c>HybridSql</c> (ADR-025).
    ///
    /// <para>
    /// RRF is rank-based on purpose: cosine similarity and <c>ts_rank_cd</c> are not on a comparable
    /// scale, and their distributions differ per query, so rank fusion cannot be skewed by a leg
    /// whose raw scores happen to be large. A note found by only one leg still ranks, which is what
    /// makes the hybrid more recall-capable than either leg alone.
    /// </para>
    ///
    /// <para>
    /// Written as a single SELECT with derived tables rather than a CTE: EF Core composes
    /// <c>SqlQueryRaw</c> by wrapping the statement in a subquery, and a leading <c>WITH</c> would
    /// not survive that.
    /// </para>
    ///
    /// <para>
    /// Placeholders: {0} query text, {1} embedding literal, {2} organisation id, {3} customer id,
    /// {4} candidate pool, {5} fusion constant, {6} top-k, {7} minimum cosine similarity.
    /// </para>
    /// </summary>
    private const string HybridSql = """
        SELECT c."Id",
               c."CustomerId",
               c."Content",
               c."Category",
               c."Source",
               c."Confidence",
               c."IsExplicit",
               vec.similarity AS "Similarity",
               vec.rank AS "VectorRank",
               lex.rank AS "LexicalRank",
               (COALESCE(1.0 / ({5} + vec.rank), 0) + COALESCE(1.0 / ({5} + lex.rank), 0))::double precision AS "Score"
        FROM "CustomerMemory" c
        LEFT JOIN (
            SELECT v."Id",
                   1 - (v.embedding <=> CAST({1} AS vector)) AS similarity,
                   ROW_NUMBER() OVER (ORDER BY v.embedding <=> CAST({1} AS vector)) AS rank
            FROM "CustomerMemory" v
            WHERE v."OrganizationId" = {2}
              AND v."CustomerId" = {3}
              AND v."DeletedAt" IS NULL
              AND v.embedding IS NOT NULL
              AND (v."ExpiresAt" IS NULL OR v."ExpiresAt" > now())
              AND 1 - (v.embedding <=> CAST({1} AS vector)) >= {7}
            ORDER BY v.embedding <=> CAST({1} AS vector)
            LIMIT {4}
        ) vec ON vec."Id" = c."Id"
        LEFT JOIN (
            SELECT l."Id",
                   ROW_NUMBER() OVER (
                       ORDER BY ts_rank_cd(l."SearchVector", websearch_to_tsquery('english'::regconfig, {0})) DESC
                   ) AS rank
            FROM "CustomerMemory" l
            WHERE l."OrganizationId" = {2}
              AND l."CustomerId" = {3}
              AND l."DeletedAt" IS NULL
              AND (l."ExpiresAt" IS NULL OR l."ExpiresAt" > now())
              AND l."SearchVector" @@ websearch_to_tsquery('english'::regconfig, {0})
            ORDER BY ts_rank_cd(l."SearchVector", websearch_to_tsquery('english'::regconfig, {0})) DESC
            LIMIT {4}
        ) lex ON lex."Id" = c."Id"
        WHERE c."OrganizationId" = {2}
          AND c."CustomerId" = {3}
          AND c."DeletedAt" IS NULL
          AND (c."ExpiresAt" IS NULL OR c."ExpiresAt" > now())
          AND (vec."Id" IS NOT NULL OR lex."Id" IS NOT NULL)
        ORDER BY "Score" DESC
        LIMIT {6}
        """;

    /// <summary>
    /// The lexical leg alone. Used by <c>mode=lexical</c> and as the hybrid fallback when no query
    /// embedding could be produced, so a provider outage degrades instead of failing. The same RRF
    /// constant is applied to the single leg so scores stay comparable with the hybrid path.
    ///
    /// <para>Placeholders: {0} query text, {1} organisation id, {2} customer id, {3} candidate pool,
    /// {4} top-k, {5} fusion constant.</para>
    /// </summary>
    private const string LexicalOnlySql = """
        SELECT t."Id",
               t."CustomerId",
               t."Content",
               t."Category",
               t."Source",
               t."Confidence",
               t."IsExplicit",
               NULL::double precision AS "Similarity",
               NULL::bigint AS "VectorRank",
               t.rank AS "LexicalRank",
               (1.0 / ({5} + t.rank))::double precision AS "Score"
        FROM (
            SELECT l."Id",
                   l."CustomerId",
                   l."Content",
                   l."Category",
                   l."Source",
                   l."Confidence",
                   l."IsExplicit",
                   ROW_NUMBER() OVER (
                       ORDER BY ts_rank_cd(l."SearchVector", websearch_to_tsquery('english'::regconfig, {0})) DESC
                   ) AS rank
            FROM "CustomerMemory" l
            WHERE l."OrganizationId" = {1}
              AND l."CustomerId" = {2}
              AND l."DeletedAt" IS NULL
              AND (l."ExpiresAt" IS NULL OR l."ExpiresAt" > now())
              AND l."SearchVector" @@ websearch_to_tsquery('english'::regconfig, {0})
            ORDER BY ts_rank_cd(l."SearchVector", websearch_to_tsquery('english'::regconfig, {0})) DESC
            LIMIT {3}
        ) t
        ORDER BY "Score" DESC
        LIMIT {4}
        """;

    /// <summary>
    /// The dense leg alone, for <c>mode=vector</c>. Its score is the cosine similarity itself,
    /// because that is the quantity the caller is asking about when it asks for the vector leg, and
    /// the <c>minSimilarity</c> floor applies here as it did before the hybrid existed.
    ///
    /// <para>Placeholders: {0} embedding literal, {1} organisation id, {2} customer id, {3}
    /// candidate pool, {4} top-k, {5} minimum cosine similarity.</para>
    /// </summary>
    private const string VectorOnlySql = """
        SELECT v."Id",
               v."CustomerId",
               v."Content",
               v."Category",
               v."Source",
               v."Confidence",
               v."IsExplicit",
               v.similarity AS "Similarity",
               v.rank AS "VectorRank",
               NULL::bigint AS "LexicalRank",
               v.similarity::double precision AS "Score"
        FROM (
            SELECT c."Id",
                   c."CustomerId",
                   c."Content",
                   c."Category",
                   c."Source",
                   c."Confidence",
                   c."IsExplicit",
                   1 - (c.embedding <=> CAST({0} AS vector)) AS similarity,
                   ROW_NUMBER() OVER (ORDER BY c.embedding <=> CAST({0} AS vector)) AS rank
            FROM "CustomerMemory" c
            WHERE c."OrganizationId" = {1}
              AND c."CustomerId" = {2}
              AND c."DeletedAt" IS NULL
              AND c.embedding IS NOT NULL
              AND (c."ExpiresAt" IS NULL OR c."ExpiresAt" > now())
              AND 1 - (c.embedding <=> CAST({0} AS vector)) >= {5}
            ORDER BY c.embedding <=> CAST({0} AS vector)
            LIMIT {3}
        ) v
        ORDER BY "Score" DESC
        LIMIT {4}
        """;

    /// <summary>The dense candidate pool each leg contributes before fusion (the handbook default).</summary>
    private const int DefaultCandidatePool = 20;

    /// <summary>The Reciprocal Rank Fusion constant; the handbook's default, so the scores agree.</summary>
    private const int DefaultFusionK = 60;

    public async Task SaveAsync(CustomerMemory memory, CancellationToken cancellationToken = default)
    {
        EnsureContentKey(memory);
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
        public string Source { get; set; } = "conversation";
        public decimal Confidence { get; set; }
        public bool IsExplicit { get; set; }
        public double? Similarity { get; set; }
        public long? VectorRank { get; set; }
        public long? LexicalRank { get; set; }
        public double Score { get; set; }
    }
}
