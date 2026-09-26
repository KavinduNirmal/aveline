using Aveline.Api.Modules.VisualIntelligence.Models;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public interface IInventoryRepository
{
    Task<InventoryItem?> GetByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default);
    Task<InventoryItem?> GetBySkuAsync(string sku, Guid orgId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryItem>> SearchAsync(
        Guid orgId,
        string? category = null,
        string? color = null,
        string? size = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        bool inStockOnly = true,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<InventoryItem> Items, int Total)> QueryAsync(
        Guid orgId,
        DTOs.CatalogQueryRequest request,
        CancellationToken cancellationToken = default);
    Task<DTOs.CatalogFacetsResponse> GetFacetsAsync(
        Guid orgId,
        DTOs.CatalogQueryRequest? currentNarrowing = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryItem>> GetLowStockAsync(
        Guid orgId,
        int threshold = 5,
        CancellationToken cancellationToken = default);
    Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default);
    Task UpdateAsync(InventoryItem item, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default);

    Task<InventoryImage?> GetImageByIdAsync(Guid imageId, Guid orgId, CancellationToken cancellationToken = default);
    Task AddImageAsync(InventoryImage image, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the image row. The caller tells the image store first, so a provider that keeps
    /// bytes elsewhere releases them before the row that names them disappears
    /// (<c>AttachmentSweepJob.cs:74-78</c>). This does not touch the item.
    /// </summary>
    Task DeleteImageAsync(Guid imageId, Guid orgId, CancellationToken cancellationToken = default);
}
