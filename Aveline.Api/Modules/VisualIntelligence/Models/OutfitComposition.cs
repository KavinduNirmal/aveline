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

