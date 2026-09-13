namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class CreateInventoryItemDto
{
    public Guid OrganizationId { get; set; }
    public Guid OrgId
    {
        get => OrganizationId;
        set => OrganizationId = value;
    }
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
}
