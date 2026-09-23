using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Handbook.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Handbook.Repositories;

/// <summary>
/// Data access for <see cref="HandbookChunk"/>. The two search columns are external to the EF
/// model (ADR-017, ADR-025) and are only reachable through the raw SQL below.
/// </summary>
public class HandbookRepository : IHandbookRepository
{
    private const string VectorKindsToken = "/*VECTOR_KINDS*/";
    private const string LexicalKindsToken = "/*LEXICAL_KINDS*/";

    private const string HybridMode = "hybrid";
    private const string LexicalMode = "lexical";
    private const string VectorMode = "vector";

    private readonly AppDbContext _context;

    public HandbookRepository(AppDbContext context) => _context = context;

    public async Task<HandbookChunk?> GetBySourceOrdinalAsync(
        string sourceKey,
        int ordinal,
        CancellationToken cancellationToken = default)
        => await _context.HandbookChunks
            .FirstOrDefaultAsync(c => c.SourceKey == sourceKey && c.Ordinal == ordinal, cancellationToken);

    public async Task<HandbookChunk> AddAsync(
        HandbookChunk chunk,
        CancellationToken cancellationToken = default)
    {
        _context.HandbookChunks.Add(chunk);
        await _context.SaveChangesAsync(cancellationToken);
        return chunk;
    }

    public async Task<HandbookChunk> UpdateAsync(
        HandbookChunk chunk,
        CancellationToken cancellationToken = default)
    {
        chunk.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return chunk;
    }

    public async Task UpdateEmbeddingAsync(
        Guid chunkId,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        // Embeddings only exist on PostgreSQL (pgvector). No-op elsewhere (e.g. the in-memory test
        // provider) so callers can persist chunks without a live vector column.
        if (!_context.Database.IsRelational())
        {
            return;
        }

        // Serialize the vector in pgvector literal form: [a,b,c].
        var literal = "[" + string.Join(",", embedding) + "]";
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE \"HandbookChunk\" SET embedding = CAST({0} AS vector) WHERE \"Id\" = {1}",
            new object[] { literal, chunkId },
            cancellationToken);
    }

    public async Task<int> DeleteBySourceAsync(
        string sourceKey,
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.HandbookChunks
            .Where(c => c.SourceKey == sourceKey)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return 0;
        }

        _context.HandbookChunks.RemoveRange(rows);
        await _context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<IReadOnlyList<HandbookSourceSummary>> ListSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.HandbookChunks
            .GroupBy(c => new { c.SourceKey, c.SourceKind, c.SourceTitle })
            .Select(g => new
            {
                g.Key.SourceKey,
                g.Key.SourceKind,
                g.Key.SourceTitle,
                ChunkCount = g.Count(),
                UpdatedAt = g.Max(c => c.UpdatedAt),
            })
            .OrderBy(r => r.SourceKey)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new HandbookSourceSummary(
                r.SourceKey, r.SourceKind, r.SourceTitle, r.ChunkCount, r.UpdatedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<HandbookSearchHit>> SearchAsync(
        HandbookSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsRelational())
        {
            throw new InvalidOperationException(
                "Handbook search requires a relational (PostgreSQL + pgvector) provider.");
        }

        var rows = query.Mode switch
        {
            LexicalMode => await SearchLexicalOnlyAsync(query, cancellationToken),
            VectorMode => query.QueryEmbedding is null
                // An explicit vector request with no vector cannot answer; returning nothing is
                // honest, where silently answering lexically would misreport what was measured.
                ? []
                : await SearchVectorOnlyAsync(query, cancellationToken),
            _ => query.QueryEmbedding is null
                // The embedding provider was unavailable. Hybrid degrades to the lexical leg
                // rather than failing; `VectorRank` is NULL for every row. This is what makes a
                // provider outage a degradation instead of an outage.
                ? await SearchLexicalOnlyAsync(query, cancellationToken)
                : await SearchHybridAsync(query, cancellationToken),
        };

        return rows;
    }

    private async Task<IReadOnlyList<HandbookSearchHit>> SearchHybridAsync(
        HandbookSearchQuery query,
        CancellationToken cancellationToken)
    {
        var (topK, pool, fusionK, audience, kinds) = Normalise(query);
        var literal = VectorLiteral(query.QueryEmbedding!);

        var sql = kinds is null
            ? HybridSql
                .Replace(VectorKindsToken, string.Empty)
                .Replace(LexicalKindsToken, string.Empty)
            : HybridSql
                .Replace(VectorKindsToken, "AND v.\"SourceKind\" = ANY({7})")
                .Replace(LexicalKindsToken, "AND l.\"SourceKind\" = ANY({7})");
        var args = kinds is null
            ? new object[] { query.Query, literal, audience, pool, fusionK, topK, query.MinSimilarity }
            : new object[] { query.Query, literal, audience, pool, fusionK, topK, query.MinSimilarity, kinds };

        return await RunAsync(sql, args, cancellationToken);
    }

    private async Task<IReadOnlyList<HandbookSearchHit>> SearchLexicalOnlyAsync(
        HandbookSearchQuery query,
        CancellationToken cancellationToken)
    {
        var (_, pool, fusionK, audience, kinds) = Normalise(query);

        var sql = kinds is null
            ? LexicalOnlySql.Replace(LexicalKindsToken, string.Empty)
            : LexicalOnlySql.Replace(LexicalKindsToken, "AND l.\"SourceKind\" = ANY({4})");
        var args = kinds is null
            ? new object[] { query.Query, audience, fusionK, pool }
            : new object[] { query.Query, audience, fusionK, pool, kinds };

        return await RunAsync(sql, args, cancellationToken);
    }

    private async Task<IReadOnlyList<HandbookSearchHit>> SearchVectorOnlyAsync(
        HandbookSearchQuery query,
        CancellationToken cancellationToken)
    {
        var (_, pool, _, audience, kinds) = Normalise(query);
        var literal = VectorLiteral(query.QueryEmbedding!);

        var sql = kinds is null
            ? VectorOnlySql.Replace(VectorKindsToken, string.Empty)
            : VectorOnlySql.Replace(VectorKindsToken, "AND c.\"SourceKind\" = ANY({4})");
        var args = kinds is null
            ? new object[] { literal, audience, pool, query.MinSimilarity }
            : new object[] { literal, audience, pool, query.MinSimilarity, kinds };

        return await RunAsync(sql, args, cancellationToken);
    }

    private async Task<IReadOnlyList<HandbookSearchHit>> RunAsync(
        string sql,
        object[] args,
        CancellationToken cancellationToken)
    {
        var rows = await _context.Database
            .SqlQueryRaw<HandbookSearchRow>(sql, args)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new HandbookSearchHit(
                r.Id,
                r.SourceKey,
                r.SourceKind,
                r.SourceTitle,
                r.SourceUrl,
                r.HeadingPath,
                r.Anchor,
                r.Content,
                r.VectorRank,
                r.LexicalRank,
                r.Score))
            .ToList();
    }

    private static (int TopK, int Pool, int FusionK, string Audience, string[]? Kinds) Normalise(
        HandbookSearchQuery query)
    {
        var topK = Math.Max(query.TopK, 1);
        return (
            topK,
            Math.Max(query.CandidatePool, topK),
            Math.Max(query.FusionK, 1),
            string.IsNullOrWhiteSpace(query.Audience) ? "staff" : query.Audience,
            query.SourceKinds is { Count: > 0 } ? query.SourceKinds.ToArray() : null);
    }

    private static string VectorLiteral(float[] embedding)
        => "[" + string.Join(",", embedding) + "]";

    /// <summary>
    /// The hybrid statement: one dense leg (pgvector cosine) and one lexical leg (PostgreSQL
    /// full-text) over the same filter, fused with Reciprocal Rank Fusion.
    ///
    /// <para>
    /// RRF is rank-based on purpose: cosine similarity and <c>ts_rank_cd</c> are not on a
    /// comparable scale, and their distributions differ per query, so rank fusion cannot be skewed
    /// by a leg whose raw scores happen to be large. A document found by only one leg still ranks,
    /// which is what makes the hybrid more recall-capable than either leg alone.
    /// </para>
    ///
    /// <para>
    /// Written as a single SELECT with derived tables rather than a CTE: EF Core composes
    /// <c>SqlQueryRaw</c> by wrapping the statement in a subquery, and a leading <c>WITH</c> would
    /// not survive that.
    /// </para>
    ///
    /// <para>
    /// Placeholders: {0} query text, {1} embedding literal, {2} audience, {3} candidate pool,
    /// {4} fusion constant, {5} top-k, {6} minimum cosine similarity, {7} source kinds (only when
    /// the kind tokens are substituted).
    /// </para>
    /// </summary>
    private const string HybridSql = """
        SELECT c."Id",
               c."SourceKey",
               c."SourceKind",
               c."SourceTitle",
               c."SourceUrl",
               c."HeadingPath",
               c."Anchor",
               c."Content",
               vec.rank AS "VectorRank",
               lex.rank AS "LexicalRank",
               (COALESCE(1.0 / ({4} + vec.rank), 0) + COALESCE(1.0 / ({4} + lex.rank), 0))::double precision AS "Score"
        FROM "HandbookChunk" c
        LEFT JOIN (
            SELECT v."Id",
                   ROW_NUMBER() OVER (ORDER BY v.embedding <=> CAST({1} AS vector)) AS rank
            FROM "HandbookChunk" v
            WHERE v."IsActive"
              AND (v."Audience" = {2} OR v."Audience" = 'both')
              AND v.embedding IS NOT NULL
              AND (1 - (v.embedding <=> CAST({1} AS vector))) >= {6}
              /*VECTOR_KINDS*/
            ORDER BY v.embedding <=> CAST({1} AS vector)
            LIMIT {3}
        ) vec ON vec."Id" = c."Id"
        LEFT JOIN (
            SELECT l."Id",
                   ROW_NUMBER() OVER (
                       ORDER BY ts_rank_cd(l."SearchVector", websearch_to_tsquery('english'::regconfig, {0})) DESC
                   ) AS rank
            FROM "HandbookChunk" l
            WHERE l."IsActive"
              AND (l."Audience" = {2} OR l."Audience" = 'both')
              AND l."SearchVector" @@ websearch_to_tsquery('english'::regconfig, {0})
              /*LEXICAL_KINDS*/
            ORDER BY ts_rank_cd(l."SearchVector", websearch_to_tsquery('english'::regconfig, {0})) DESC
            LIMIT {3}
        ) lex ON lex."Id" = c."Id"
        WHERE vec."Id" IS NOT NULL OR lex."Id" IS NOT NULL
        ORDER BY "Score" DESC
        LIMIT {5}
        """;

    /// <summary>
    /// The lexical leg alone. Used by <c>mode=lexical</c> and as the hybrid fallback when no query
    /// embedding could be produced, so a provider outage degrades instead of failing. The same RRF
    /// constant is applied to the single leg so scores stay comparable with the hybrid path.
    ///
    /// <para>Placeholders: {0} query text, {1} audience, {2} fusion constant, {3} candidate pool,
    /// {4} source kinds (only when the kind token is substituted).</para>
    /// </summary>
    private const string LexicalOnlySql = """
        SELECT t."Id",
               t."SourceKey",
               t."SourceKind",
               t."SourceTitle",
               t."SourceUrl",
               t."HeadingPath",
               t."Anchor",
               t."Content",
               NULL::bigint AS "VectorRank",
               t.rank AS "LexicalRank",
               (1.0 / ({2} + t.rank))::double precision AS "Score"
        FROM (
            SELECT l."Id",
                   l."SourceKey",
                   l."SourceKind",
                   l."SourceTitle",
                   l."SourceUrl",
                   l."HeadingPath",
                   l."Anchor",
                   l."Content",
                   ROW_NUMBER() OVER (
                       ORDER BY ts_rank_cd(l."SearchVector", websearch_to_tsquery('english'::regconfig, {0})) DESC
                   ) AS rank
            FROM "HandbookChunk" l
            WHERE l."IsActive"
              AND (l."Audience" = {1} OR l."Audience" = 'both')
              AND l."SearchVector" @@ websearch_to_tsquery('english'::regconfig, {0})
              /*LEXICAL_KINDS*/
            ORDER BY ts_rank_cd(l."SearchVector", websearch_to_tsquery('english'::regconfig, {0})) DESC
            LIMIT {3}
        ) t
        ORDER BY "Score" DESC
        """;

    /// <summary>
    /// The dense leg alone, for <c>mode=vector</c>. Its score is the cosine similarity itself,
    /// because that is the quantity the caller is asking about when it asks for the vector leg.
    ///
    /// <para>Placeholders: {0} embedding literal, {1} audience, {2} candidate pool, {3} minimum
    /// cosine similarity, {4} source kinds (only when the kind token is substituted).</para>
    /// </summary>
    private const string VectorOnlySql = """
        SELECT v."Id",
               v."SourceKey",
               v."SourceKind",
               v."SourceTitle",
               v."SourceUrl",
               v."HeadingPath",
               v."Anchor",
               v."Content",
               v.rank AS "VectorRank",
               NULL::bigint AS "LexicalRank",
               (1 - (v.embedding <=> CAST({0} AS vector)))::double precision AS "Score"
        FROM (
            SELECT c."Id",
                   c."SourceKey",
                   c."SourceKind",
                   c."SourceTitle",
                   c."SourceUrl",
                   c."HeadingPath",
                   c."Anchor",
                   c."Content",
                   c.embedding,
                   ROW_NUMBER() OVER (ORDER BY c.embedding <=> CAST({0} AS vector)) AS rank
            FROM "HandbookChunk" c
            WHERE c."IsActive"
              AND (c."Audience" = {1} OR c."Audience" = 'both')
              AND c.embedding IS NOT NULL
              AND (1 - (c.embedding <=> CAST({0} AS vector))) >= {3}
              /*VECTOR_KINDS*/
            ORDER BY c.embedding <=> CAST({0} AS vector)
            LIMIT {2}
        ) v
        ORDER BY "Score" DESC
        """;

    /// <summary>
    /// Row projection for the raw search SQL. Public with settable properties because EF Core
    /// materialises it by column name.
    /// </summary>
    public sealed class HandbookSearchRow
    {
        public Guid Id { get; set; }
        public string SourceKey { get; set; } = string.Empty;
        public string SourceKind { get; set; } = string.Empty;
        public string SourceTitle { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string HeadingPath { get; set; } = string.Empty;
        public string? Anchor { get; set; }
        public string Content { get; set; } = string.Empty;
        public long? VectorRank { get; set; }
        public long? LexicalRank { get; set; }
        public double Score { get; set; }
    }
}
