using System.ComponentModel.DataAnnotations;
using Aveline.Api.Modules.Handbook.Models;
using Aveline.Api.Modules.Handbook.Repositories;

namespace Aveline.Api.Modules.Handbook.DTOs;

/// <summary>
/// Upsert one handbook chunk. The chunker computes <see cref="ContentHash"/>; the server embeds
/// <see cref="Content"/> itself, so a caller never sends a vector.
/// </summary>
public sealed record SaveHandbookChunkRequest
{
    [Required, MaxLength(160)]
    public string SourceKey { get; init; } = string.Empty;

    [MaxLength(32)]
    public string SourceKind { get; init; } = "company";

    [Required, MaxLength(200)]
    public string SourceTitle { get; init; } = string.Empty;

    [MaxLength(300)]
    public string SourceUrl { get; init; } = string.Empty;

    /// <summary>Heading trail, e.g. "Pieces &gt; Add a piece".</summary>
    [MaxLength(300)]
    public string HeadingPath { get; init; } = string.Empty;

    [MaxLength(160)]
    public string? Anchor { get; init; }

    [Required]
    public string Content { get; init; } = string.Empty;

    [Required, MaxLength(64)]
    public string ContentHash { get; init; } = string.Empty;

    [MaxLength(16)]
    public string Audience { get; init; } = "staff";

    /// <summary>Position within the source. Together with <see cref="SourceKey"/> this is the key.</summary>
    public int Ordinal { get; init; }

    public string TagsJson { get; init; } = "{}";
}

/// <summary>Hybrid (dense + lexical) search over the handbook.</summary>
public sealed record HandbookSearchRequest
{
    [Required, MaxLength(500)]
    public string Query { get; init; } = string.Empty;

    public int TopK { get; init; } = 5;

    /// <summary>
    /// hybrid (default) | lexical | vector. The single-leg modes exist for retrieval evaluation;
    /// the agent always asks for the default.
    /// </summary>
    [MaxLength(16)]
    public string Mode { get; init; } = "hybrid";

    /// <summary>staff | customer | both. Retrieval defaults to the staff audience.</summary>
    [MaxLength(16)]
    public string Audience { get; init; } = "staff";

    /// <summary>Cosine floor applied to the vector leg only; a post-fusion floor would delete lexical-only hits.</summary>
    public double MinSimilarity { get; init; }

    /// <summary>Restrict to these source kinds (web-docs | company | legal). Empty means all.</summary>
    public IReadOnlyList<string>? SourceKinds { get; init; }
}

public sealed record HandbookChunkDto(
    Guid Id,
    string SourceKey,
    string SourceKind,
    string SourceTitle,
    string SourceUrl,
    string HeadingPath,
    string? Anchor,
    string Audience,
    int Ordinal,
    bool IsActive,
    string ContentHash,
    DateTime UpdatedAt)
{
    public static HandbookChunkDto From(HandbookChunk chunk) => new(
        chunk.Id,
        chunk.SourceKey,
        chunk.SourceKind,
        chunk.SourceTitle,
        chunk.SourceUrl,
        chunk.HeadingPath,
        chunk.Anchor,
        chunk.Audience,
        chunk.Ordinal,
        chunk.IsActive,
        chunk.ContentHash,
        chunk.UpdatedAt);
}

/// <summary>
/// A fused hybrid hit. Both legs' ranks are exposed, not just the fused score: that is what lets
/// the retrieval eval report vector, lexical and hybrid recall separately instead of one opaque
/// number (ADR-025).
/// </summary>
public sealed record HandbookSearchResultDto(
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
    double Score)
{
    public static HandbookSearchResultDto From(HandbookSearchHit hit) => new(
        hit.Id,
        hit.SourceKey,
        hit.SourceKind,
        hit.SourceTitle,
        hit.SourceUrl,
        hit.HeadingPath,
        hit.Anchor,
        hit.Content,
        hit.VectorRank,
        hit.LexicalRank,
        hit.Score);
}

/// <summary>One indexed source, for ops visibility into what the index actually holds.</summary>
public sealed record HandbookSourceSummaryDto(
    string SourceKey,
    string SourceKind,
    string SourceTitle,
    int ChunkCount,
    DateTime UpdatedAt);
