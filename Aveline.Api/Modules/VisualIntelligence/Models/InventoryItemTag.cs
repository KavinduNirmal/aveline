using System;

namespace Aveline.Api.Modules.VisualIntelligence.Models;

/// <summary>
/// Join entity between an inventory piece and a boutique catalog tag.
/// </summary>
public class InventoryItemTag
{
    public Guid ItemId { get; set; }
    public InventoryItem Item { get; set; } = null!;

    public Guid TagId { get; set; }
    public CatalogTag Tag { get; set; } = null!;

    public Guid OrgId { get; set; }
}
