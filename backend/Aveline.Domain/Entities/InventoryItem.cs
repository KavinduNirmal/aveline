namespace Aveline.Domain.Entities;

public class InventoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public List<string> Sizes { get; set; } = new();
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string Status { get; set; } = "available";
    public string? ImageUrl { get; set; }
    public string? Sku { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public DateTime? DeletedAt { get; set; }
    public bool IsAvailable => Quantity > 0 && Status == "available" && DeletedAt == null;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
