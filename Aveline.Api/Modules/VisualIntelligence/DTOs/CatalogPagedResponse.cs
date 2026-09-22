namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class CatalogPagedResponse
{
    public IReadOnlyList<InventoryItemDto> Items { get; set; } = Array.Empty<InventoryItemDto>();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
