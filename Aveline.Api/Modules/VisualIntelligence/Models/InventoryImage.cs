using System;
using System.Collections.Generic;

namespace Aveline.Api.Modules.VisualIntelligence.Models;

public class InventoryImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public Guid? ItemId { get; set; }

    /// <summary><c>database</c> today; a CDN provider name once that adapter lands.</summary>
    public string StorageProvider { get; set; } = "database";

    /// <summary>The provider's own key. The database adapter leaves this null.</summary>
    public string? StorageKey { get; set; }

    public byte[]? ImageData { get; set; }
    public string ContentType { get; set; } = "image/jpeg";
    public string? FileName { get; set; }
    public long? FileSizeBytes { get; set; }
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
