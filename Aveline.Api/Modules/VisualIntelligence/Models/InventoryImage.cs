namespace Aveline.Api.Modules.VisualIntelligence.Models;

public class InventoryImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public Guid InventoryItemId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? DominantColor { get; set; }
    public string? DetectedFabric { get; set; }
    public string? DetectedPattern { get; set; }
    public string? DetectedStyle { get; set; }
    public double? ConfidenceScore { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
