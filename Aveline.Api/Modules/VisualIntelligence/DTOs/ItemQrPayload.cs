using System;
using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

/// <summary>
/// Canonical structured JSON envelope embedded inside boutique floor-tag QR codes.
/// </summary>
public class ItemQrPayload
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "aveline_inventory_item";

    [JsonPropertyName("orgId")]
    public Guid OrgId { get; set; }

    [JsonPropertyName("itemId")]
    public Guid ItemId { get; set; }

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("v")]
    public int Version { get; set; } = 1;
}
