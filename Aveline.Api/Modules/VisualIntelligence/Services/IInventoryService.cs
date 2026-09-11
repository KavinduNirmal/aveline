using Aveline.Api.Modules.VisualIntelligence.DTOs;

namespace Aveline.Api.Modules.VisualIntelligence.Services;

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryItemDto>> SearchInventoryAsync(SearchInventoryDto request, CancellationToken cancellationToken = default);
    Task<InventoryItemDto?> GetItemByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default);
    Task<InventoryItemDto> CreateItemAsync(CreateInventoryItemDto dto, CancellationToken cancellationToken = default);
    Task<InventoryItemDto?> UpdateItemAsync(Guid id, UpdateInventoryItemDto dto, CancellationToken cancellationToken = default);
    Task<InventoryItemDto?> UpdateStatusAsync(Guid id, Guid orgId, string status, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryItemDto>> GetLowStockItemsAsync(Guid orgId, int threshold = 5, CancellationToken cancellationToken = default);
}
