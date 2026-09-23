namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class CatalogQueryRequest
{
    public Guid OrganizationId { get; set; }
    public Guid OrgId
    {
        get => OrganizationId;
        set => OrganizationId = value;
    }

    public string? Search { get; set; }
    public List<string>? Categories { get; set; }
    public List<string>? Fabrics { get; set; }
    public List<string>? Sizes { get; set; }
    public List<string>? Statuses { get; set; }
    public List<string>? PriceBands { get; set; }
    public List<string>? TagIds { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool InStockOnly { get; set; } = false;
    public string? Sort { get; set; } = "name:asc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
