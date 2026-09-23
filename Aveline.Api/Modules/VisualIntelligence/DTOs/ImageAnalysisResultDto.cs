using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

/// <summary>
/// The vision analysis result. Field names are pinned where a consumer outside .NET reads them.
/// </summary>
/// <remarks>
/// <para>
/// The application adds only a <c>JsonStringEnumConverter</c> and no naming policy, so an
/// unattributed property serialises as camelCase. <c>image_tools.py:15</c> reads
/// <c>primary_color</c> (then <c>color</c>) and falls back to <c>"Neutral"</c>, so without the
/// attribute the extracted colour was silently lost on the wire (migration plan §8.2, strategy §4
/// C26). The attribute names the property the Python side already prefers first and does not
/// rename the C# member, so the Python defaults stay the defensive fallback they are.
/// </para>
/// <para>
/// <b><see cref="SecondaryColors"/> carries the same attribute for the same reason.</b> The Elle
/// plan's G5 and §6.1 name <c>secondaryColors</c> alongside <c>primaryColor</c> as "nulled by the
/// casing bug": <c>image_tools.py:58</c> reads <c>secondary_colors</c> and falls back to
/// <c>[]</c>, so the camelCase spelling was dropped silently across the boundary. No other reader
/// consumes <c>secondaryColors</c> - the web client reads <c>suggestedKeywords</c>,
/// <c>confidenceScore</c>, <c>garmentType</c> and <c>suggestedItemName</c>
/// (<c>frontend/web/src/lib/catalog-api.ts:403-415</c>) and never the secondary palette - so the
/// rename is confined to the boundary that was losing the value. The fields the canonical
/// dual-casing mapping in the Elle plan §5.7 already accepts under camelCase
/// (<c>suggestedKeywords</c>, <c>confidenceScore</c>, <c>garmentType</c>, <c>colorHex</c>,
/// <c>suggestedItemName</c>, <c>stylingNotes</c>) keep their spelling, because the web client
/// depends on it.
/// </remarks>
public class ImageAnalysisResultDto
{
    public string Category { get; set; } = string.Empty;

    /// <summary>The extracted dominant colour; on the wire as <c>primary_color</c>.</summary>
    [JsonPropertyName("primary_color")]
    public string PrimaryColor { get; set; } = string.Empty;

    public string? ColorHex { get; set; }

    /// <summary>The accent colours; on the wire as <c>secondary_colors</c> (<c>image_tools.py:58</c>).</summary>
    [JsonPropertyName("secondary_colors")]
    public List<string> SecondaryColors { get; set; } = new();
    public string? Pattern { get; set; }
    public string? Style { get; set; }
    public string? Fabric { get; set; }
    public string? GarmentType { get; set; }
    public string? SuggestedItemName { get; set; }
    public string? Description { get; set; }
    public string? StylingNotes { get; set; }
    public double ConfidenceScore { get; set; } = 0.95;
    public bool IsFallback { get; set; } = false;

    /// <summary>
    /// The referenced asset's stored type is outside the provider's analysable subset, so no
    /// analysis was attempted and no access was minted (strategy §3.6). Carried explicitly so a
    /// caller can tell "this format cannot be analysed" from "the provider failed"
    /// (<see cref="IsFallback"/>) instead of the two collapsing into one silent placeholder.
    /// </summary>
    [JsonPropertyName("not_analysable")]
    public bool NotAnalysable { get; set; } = false;

    public List<string> SuggestedKeywords { get; set; } = new();
}
