using Aveline.Api.Modules.VisualIntelligence.Models;

namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class InventoryItemDto
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid OrganizationId
    {
        get => OrgId;
        set => OrgId = value;
    }
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string? ColorHex { get; set; }
    public List<string> Sizes { get; set; } = new();
    public decimal Price { get; set; }
    public decimal Cost { get; set; }
    public int Quantity { get; set; }
    public string Status { get; set; } = "available";
    public string? Fabric { get; set; }
    public string? Style { get; set; }
    public string? ImageUrl { get; set; }
    public string? Sku { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public DateTime? DeletedAt { get; set; }
    public bool IsAvailable { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<string> Tags { get; set; } = new();

    public static InventoryItemDto FromDomain(InventoryItem item)
    {
        return new InventoryItemDto
        {
            Id = item.Id,
            OrgId = item.OrgId,
            ItemName = item.ItemName,
            Category = item.Category,
            Color = item.Color,
            ColorHex = item.ColorHex,
            Sizes = item.Sizes?.ToList() ?? new List<string>(),
            Price = item.Price,
            Cost = item.Cost,
            Quantity = item.Quantity,
            Status = item.Status,
            Fabric = item.Fabric,
            Style = item.Style,
            ImageUrl = item.ImageUrl,
            Sku = item.Sku,
            Description = item.Description,
            Metadata = item.Metadata,
            DeletedAt = item.DeletedAt,
            IsAvailable = item.IsAvailable,
            CreatedAtUtc = item.CreatedAtUtc,
            Tags = item.ItemTags?.Where(it => it.Tag != null).Select(it => it.Tag.Slug).ToList() ?? new List<string>()
        };
    }
}
