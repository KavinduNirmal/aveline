using Aveline.Application.DTOs.Inventory;

namespace Aveline.Application.DTOs.Visual;

public class ComposedOutfitDto
{
    public string LookName { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string Occasion { get; set; } = string.Empty;
    public string StylingNotes { get; set; } = string.Empty;
    public InventoryItemDto PrimaryItem { get; set; } = null!;
    public List<InventoryItemDto> ComplementaryItems { get; set; } = new();
    public decimal TotalLookPrice { get; set; }
}
