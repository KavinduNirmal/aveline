namespace Aveline.Api.Modules.VisualIntelligence.Models;

public class OutfitComposition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public Guid CustomerId { get; set; }
    public string Occasion { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public List<OutfitItem> Items { get; set; } = new();
    public string? StyleNotes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class OutfitItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OutfitCompositionId { get; set; }
    public Guid InventoryItemId { get; set; }
    public string Role { get; set; } = string.Empty; // e.g., "top", "bottom", "shoes", "accessory"
}
