namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class UpdateInventoryItemDto
{
    public Guid OrganizationId { get; set; }
    public Guid OrgId
    {
        get => OrganizationId;
        set => OrganizationId = value;
    }
    public string? ItemName { get; set; }
    public string? Category { get; set; }
    public string? Color { get; set; }
    public List<string>? Sizes { get; set; }
    public decimal? Price { get; set; }
    public int? Quantity { get; set; }
    public string? Status { get; set; }
    public string? ImageUrl { get; set; }
    public string? Sku { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
