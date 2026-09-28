namespace Aveline.Api.Modules.Shared.DTOs;

/// <summary>
/// A single hit in a cross-entity search response.
/// </summary>
public sealed record SearchResultItemDto(
    string Type,
    Guid Id,
    string Title,
    string? Subtitle,
    double Score,
    string Href);

/// <summary>
/// A paged cross-entity search result envelope.
/// </summary>
public sealed record SearchResultPageDto(
    IReadOnlyList<SearchResultItemDto> Items,
    int Total,
    int Page,
    int PageSize);
