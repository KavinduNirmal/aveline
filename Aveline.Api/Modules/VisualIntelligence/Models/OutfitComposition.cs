using System;
using System.Collections.Generic;

namespace Aveline.Api.Modules.VisualIntelligence.Models;

public class OutfitComposition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public Guid? CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Occasion { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public string? StyleNotes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<OutfitItem> Items { get; set; } = new List<OutfitItem>();
}

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
