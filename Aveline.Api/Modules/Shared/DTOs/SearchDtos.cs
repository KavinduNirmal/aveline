using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.Shared.DTOs;

/// <summary>
/// The type of entity matched by cross-entity global search.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SearchEntityType
{
    [JsonStringEnumMemberName("customer")]
    Customer,

    [JsonStringEnumMemberName("catalogItem")]
    CatalogItem,

    [JsonStringEnumMemberName("conversation")]
    Conversation
}

/// <summary>
/// A single search result item aggregated from catalog pieces, customers, or conversations.
/// </summary>
public sealed record GlobalSearchResultItemDto(
    SearchEntityType Type,
    Guid Id,
    string Title,
    string? Subtitle,
    string? ThumbnailUrl,
    string Href,
    double Score);

/// <summary>
/// The paginated response envelope for global search results.
/// </summary>
public sealed record GlobalSearchResponseDto(
    IReadOnlyList<GlobalSearchResultItemDto> Items,
    int Total,
    int Page,
    int PageSize);
