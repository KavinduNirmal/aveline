using System;
using System.Collections.Generic;

namespace Aveline.Api.Modules.VisualIntelligence.Models;

/// <summary>
/// A category/style/curation tag defined by a boutique for its catalog taxonomy.
/// Scoped per organization to guarantee multi-tenant vocabulary isolation.
/// </summary>
public class CatalogTag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? ColorHex { get; set; }
    public int SortOrder { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<InventoryItemTag> ItemTags { get; set; } = new List<InventoryItemTag>();
}
