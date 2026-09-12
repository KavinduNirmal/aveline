using Aveline.Api.Modules.VisualIntelligence.DTOs;

namespace Aveline.Api.Modules.VisualIntelligence.Services;

public interface IVisualService
{
    Task<IReadOnlyList<InventoryItemDto>> SearchInventoryAsync(
        SearchInventoryDto dto,
        CancellationToken cancellationToken = default);

    Task<InventoryItemDto?> GetItemByIdAsync(
        Guid itemId,
        Guid orgId,
        CancellationToken cancellationToken = default);

    Task<InventoryItemDto> CreateInventoryItemAsync(
        CreateInventoryItemDto dto,
        CancellationToken cancellationToken = default);

    Task<InventoryItemDto?> UpdateInventoryItemAsync(
        Guid itemId,
        UpdateInventoryItemDto dto,
        CancellationToken cancellationToken = default);

    Task<InventoryItemDto?> UpdateInventoryStatusAsync(
        Guid itemId,
        UpdateInventoryStatusDto dto,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryItemDto>> GetLowStockInventoryAsync(
        Guid orgId,
        int threshold = 5,
        CancellationToken cancellationToken = default);

    Task<ImageAnalysisResultDto> AnalyzeImageAsync(
        AnalyzeImageDto dto,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerMatchDto>> GetCustomerMatchesAsync(
        Guid itemId,
        Guid orgId,
        double minScore = 0.7,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerMatchDto>> GenerateCustomerMatchesAsync(
        Guid itemId,
        GenerateCustomerMatchesDto dto,
        CancellationToken cancellationToken = default);

    Task<ComposedOutfitDto> ComposeOutfitAsync(
        ComposeOutfitDto dto,
        CancellationToken cancellationToken = default);

    Task<SourcingRequestDto> CreateSourcingRequestAsync(
        CreateSourcingRequestDto dto,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupplierCatalogItemDto>> GetSupplierCatalogAsync(
        Guid supplierId,
        Guid orgId,
        string? category = null,
        string? color = null,
        decimal? maxPrice = null,
        CancellationToken cancellationToken = default);
}
