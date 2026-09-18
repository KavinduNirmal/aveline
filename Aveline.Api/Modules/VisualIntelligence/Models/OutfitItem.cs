using System;

namespace Aveline.Api.Modules.VisualIntelligence.Models;

public class OutfitItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OutfitId { get; set; }
    public Guid ItemId { get; set; }
    public int Quantity { get; set; } = 1;
    public string Role { get; set; } = string.Empty; // e.g., "primary", "top", "bottom", "accessory"

    // Backward compatibility alias
    public Guid OutfitCompositionId
    {
        get => OutfitId;
        set => OutfitId = value;
    }
    public Guid InventoryItemId
    {
        get => ItemId;
        set => ItemId = value;
    }

    public OutfitComposition? Outfit { get; set; }
    public InventoryItem? Item { get; set; }
}
