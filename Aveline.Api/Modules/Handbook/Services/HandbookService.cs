using Aveline.Api.Modules.CustomerConcierge.Services;
using Aveline.Api.Modules.Handbook.DTOs;
using Aveline.Api.Modules.Handbook.Models;
using Aveline.Api.Modules.Handbook.Repositories;

namespace Aveline.Api.Modules.Handbook.Services;

/// <summary>
/// Chunk ingestion and hybrid retrieval over the handbook corpus (ADR-025).
///
/// <para>
/// Reuses the customer-memory <see cref="IEmbeddingService"/> rather than introducing a second
/// embedding client: the provider, the model and the 1536-dimension contract are already
/// established by ADR-017, and a second client would be a second thing to configure.
/// </para>
/// </summary>
public class HandbookService : IHandbookService
{
    private const string HybridMode = "hybrid";
    private const string LexicalMode = "lexical";
    private const string VectorMode = "vector";

    private static readonly HashSet<string> AllowedAudiences = new(StringComparer.OrdinalIgnoreCase)
    {
        "staff", "customer", "both",
    };

    private static readonly HashSet<string> AllowedModes = new(StringComparer.OrdinalIgnoreCase)
    {
        HybridMode, LexicalMode, VectorMode,
    };

    private readonly IHandbookRepository _chunks;
    private readonly IEmbeddingService _embedding;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HandbookService> _logger;

    public HandbookService(
        IHandbookRepository chunks,
        IEmbeddingService embedding,
        IConfiguration configuration,
        ILogger<HandbookService> logger)
    {
        _chunks = chunks;
        _embedding = embedding;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HandbookChunkDto> UpsertAsync(
        SaveHandbookChunkRequest request,
        CancellationToken cancellationToken = default)
    {
        var audience = NormaliseAudience(request.Audience);
        var tags = string.IsNullOrWhiteSpace(request.TagsJson) ? "{}" : request.TagsJson;

        var existing = await _chunks.GetBySourceOrdinalAsync(
            request.SourceKey, request.Ordinal, cancellationToken);

        if (existing is null)
        {
            var created = await _chunks.AddAsync(new HandbookChunk
            {
                SourceKey = request.SourceKey,
                SourceKind = request.SourceKind,
                SourceTitle = request.SourceTitle,
                SourceUrl = request.SourceUrl,
                HeadingPath = request.HeadingPath,
                Anchor = request.Anchor,
                Content = request.Content,
                ContentHash = request.ContentHash,
                Audience = audience,
                Ordinal = request.Ordinal,
                TagsJson = tags,
            }, cancellationToken);

            await EmbedChunkAsync(created.Id, request.Content, cancellationToken);
            return HandbookChunkDto.From(created);
        }

        var contentChanged = !string.Equals(existing.ContentHash, request.ContentHash, StringComparison.Ordinal);
        var metadataChanged =
            !string.Equals(existing.SourceKind, request.SourceKind, StringComparison.Ordinal)
            || !string.Equals(existing.SourceTitle, request.SourceTitle, StringComparison.Ordinal)
            || !string.Equals(existing.SourceUrl, request.SourceUrl, StringComparison.Ordinal)
            || !string.Equals(existing.HeadingPath, request.HeadingPath, StringComparison.Ordinal)
            || !string.Equals(existing.Anchor, request.Anchor, StringComparison.Ordinal)
            || !string.Equals(existing.Audience, audience, StringComparison.Ordinal)
            || !string.Equals(existing.TagsJson, tags, StringComparison.Ordinal);

        if (!contentChanged && !metadataChanged)
        {
            // A true no-op, not a touch. This is what makes re-running the seeder against unchanged
            // docs write nothing at all - no UPDATE, no re-embed, and no moved UpdatedAt.
            return HandbookChunkDto.From(existing);
        }

        existing.SourceKind = request.SourceKind;
        existing.SourceTitle = request.SourceTitle;
        existing.SourceUrl = request.SourceUrl;
        existing.HeadingPath = request.HeadingPath;
        existing.Anchor = request.Anchor;
        existing.Audience = audience;
        existing.TagsJson = tags;

        if (contentChanged)
        {
            existing.Content = request.Content;
            existing.ContentHash = request.ContentHash;
        }

        await _chunks.UpdateAsync(existing, cancellationToken);

        if (contentChanged)
        {
            await EmbedChunkAsync(existing.Id, request.Content, cancellationToken);
        }

        return HandbookChunkDto.From(existing);
    }

    public async Task<IReadOnlyList<HandbookSearchResultDto>> SearchAsync(
        HandbookSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var mode = NormaliseMode(request.Mode);
        var audience = NormaliseAudience(request.Audience);
        var topK = request.TopK > 0 ? request.TopK : _configuration.GetValue("Handbook:TopK", 5);

        // `lexical` is the one mode that must not pay for an embedding call at all.
        var embedding = string.Equals(mode, LexicalMode, StringComparison.Ordinal)
            ? null
            : await TryEmbedQueryAsync(request.Query, cancellationToken);

        if (embedding is null && !string.Equals(mode, LexicalMode, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Handbook query embedding unavailable; the {Mode} search degrades to the lexical leg.",
                mode);
        }

        var query = new HandbookSearchQuery(
            Query: request.Query,
            QueryEmbedding: embedding,
            Mode: mode,
            Audience: audience,
            TopK: topK,
            CandidatePool: _configuration.GetValue("Handbook:CandidatePool", 20),
            FusionK: _configuration.GetValue("Handbook:FusionK", 60),
            MinSimilarity: request.MinSimilarity > 0
                ? request.MinSimilarity
                : _configuration.GetValue("Handbook:MinSimilarity", 0.0),
            SourceKinds: request.SourceKinds);

        var hits = await _chunks.SearchAsync(query, cancellationToken);
        return hits.Select(HandbookSearchResultDto.From).ToList();
    }

    public Task<int> DeleteSourceAsync(
        string sourceKey,
        CancellationToken cancellationToken = default)
        => _chunks.DeleteBySourceAsync(sourceKey, cancellationToken);

    public async Task<IReadOnlyList<HandbookSourceSummaryDto>> ListSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        var sources = await _chunks.ListSourcesAsync(cancellationToken);
        return sources
            .Select(s => new HandbookSourceSummaryDto(
                s.SourceKey, s.SourceKind, s.SourceTitle, s.ChunkCount, s.UpdatedAt))
            .ToList();
    }

    /// <summary>
    /// Embeds a query, tolerating a provider failure. A failed query embedding degrades the search
    /// to the lexical leg rather than failing the request: partial results beat an error page, and
    /// the caller can see the degradation in the NULL vector ranks.
    /// </summary>
    private async Task<float[]?> TryEmbedQueryAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            return await _embedding.GenerateAsync(query, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Handbook query embedding failed.");
            return null;
        }
    }

    /// <summary>
    /// Embeds a chunk at write time. Unlike the query path this is deliberately not degradable:
    /// storing a chunk that the dense leg could never find would be worse than failing the seed
    /// loudly, and the seeder can simply be re-run.
    /// </summary>
    private async Task EmbedChunkAsync(Guid chunkId, string content, CancellationToken cancellationToken)
    {
        var embedding = await _embedding.GenerateAsync(content, cancellationToken);
        await _chunks.UpdateEmbeddingAsync(chunkId, embedding, cancellationToken);
    }

    private string NormaliseAudience(string? audience)
    {
        if (!string.IsNullOrWhiteSpace(audience) && AllowedAudiences.Contains(audience.Trim()))
        {
            return audience.Trim().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(audience))
        {
            _logger.LogWarning(
                "Unknown handbook audience {Audience}; defaulting to 'staff'.", audience);
        }

        return "staff";
    }

    private string NormaliseMode(string? mode)
    {
        if (!string.IsNullOrWhiteSpace(mode) && AllowedModes.Contains(mode.Trim()))
        {
            return mode.Trim().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(mode))
        {
            _logger.LogWarning("Unknown handbook search mode {Mode}; defaulting to hybrid.", mode);
        }

        return HybridMode;
    }
}
