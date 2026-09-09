namespace Aveline.Application.DTOs.Visual;

public class SupplierCatalogItemDto
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public decimal WholesalePrice { get; set; }
    public int LeadTimeDays { get; set; }
    public int AvailableStock { get; set; }
    public string? ImageUrl { get; set; }
}
