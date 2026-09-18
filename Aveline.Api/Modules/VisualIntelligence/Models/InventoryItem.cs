using System;
using System.Collections.Generic;

namespace Aveline.Api.Modules.VisualIntelligence.Models;

public class InventoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string? Fabric { get; set; }
    public string? Style { get; set; }
    public List<string> Sizes { get; set; } = new();
    public decimal Price { get; set; }
    public decimal Cost { get; set; }
    public int StockQuantity { get; set; }
    public string Status { get; set; } = "available";
    public string? ImageUrl { get; set; }
    public string? Sku { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    // Compatibility alias
    public int Quantity
    {
        get => StockQuantity;
        set => StockQuantity = value;
    }

    public bool IsAvailable => StockQuantity > 0 && Status == "available" && DeletedAt == null;

    public ICollection<InventoryImage> Images { get; set; } = new List<InventoryImage>();
    public ICollection<CustomerMatch> CustomerMatches { get; set; } = new List<CustomerMatch>();
}
