namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class SearchInventoryDto
{
    public Guid OrganizationId { get; set; }
    public Guid OrgId
    {
        get => OrganizationId;
        set => OrganizationId = value;
    }
    public string? Category { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public decimal? MaximumPrice
    {
        get => MaxPrice;
        set => MaxPrice = value;
    }
    public bool InStockOnly { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
