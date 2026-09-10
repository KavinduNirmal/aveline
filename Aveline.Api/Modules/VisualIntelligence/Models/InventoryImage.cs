using System;
using System.Collections.Generic;

namespace Aveline.Api.Modules.VisualIntelligence.Models;

public class InventoryImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public Guid ItemId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public string? DominantColor { get; set; }
    public string? DetectedFabric { get; set; }
    public string? DetectedPattern { get; set; }
    public string? DetectedStyle { get; set; }
    public double? ConfidenceScore { get; set; }
    public Dictionary<string, object>? AnalysisResult { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public InventoryItem? Item { get; set; }
}
