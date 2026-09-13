using Aveline.Api.Modules.VisualIntelligence.Models;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public interface IInventoryRepository
{
    Task<InventoryItem?> GetByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default);
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
    Task<IReadOnlyList<InventoryItem>> GetLowStockAsync(
        Guid orgId,
        int threshold = 5,
        CancellationToken cancellationToken = default);
    Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default);
    Task UpdateAsync(InventoryItem item, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default);
}
