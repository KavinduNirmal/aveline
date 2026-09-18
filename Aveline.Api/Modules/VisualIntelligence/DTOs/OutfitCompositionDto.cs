using System;
using System.Collections.Generic;

namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class OutfitItemDetailDto
{
    public Guid Id { get; set; }
    public Guid InventoryItemId { get; set; }
    public string Role { get; set; } = "primary";
    public string? ItemName { get; set; }
    public string? Category { get; set; }
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
}

public class OutfitCompositionDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Occasion { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public string? StyleNotes { get; set; }
    public string? HeroImageUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<OutfitItemDetailDto> Items { get; set; } = new();
}
