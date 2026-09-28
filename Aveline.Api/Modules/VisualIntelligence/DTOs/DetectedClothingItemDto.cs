using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

/// <summary>
/// A single detected clothing item/garment from multimodal image analysis.
/// Supports decomposed multi-garment outfits (e.g. shirt + jeans + jacket).
/// </summary>
public class DetectedClothingItemDto
{
    [JsonPropertyName("clothing_type")]
    public string ClothingType { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("primary_color")]
    public string PrimaryColor { get; set; } = string.Empty;

    [JsonPropertyName("color_hex")]
    public string? ColorHex { get; set; }

    [JsonPropertyName("secondary_colors")]
    public List<string> SecondaryColors { get; set; } = new();

    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    [JsonPropertyName("material")]
    public string? Material { get; set; }

    [JsonPropertyName("style")]
    public string? Style { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; } = 0.95;

    [JsonPropertyName("bounding_box")]
    public List<double>? BoundingBox { get; set; }

    [JsonPropertyName("suggested_item_name")]
    public string? SuggestedItemName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("styling_notes")]
    public string? StylingNotes { get; set; }
}
