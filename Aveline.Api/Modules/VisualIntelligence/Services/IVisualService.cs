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

    Task<bool> DeleteInventoryItemAsync(
        Guid itemId,
        Guid orgId,
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

    Task<IReadOnlyList<OutfitCompositionDto>> GetLookbooksByOrgIdAsync(
        Guid orgId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames or re-occasions a composed lookbook. Returns <c>null</c> when the id does not name a
    /// lookbook in this organisation, so a cross-tenant edit is a 404 rather than a silent write.
    /// </summary>
    Task<OutfitCompositionDto?> UpdateLookbookAsync(
        Guid id,
        Guid orgId,
        UpdateOutfitCompositionDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a composed lookbook (and, by cascade, its item rows). Returns <c>false</c> when the
    /// id does not name a lookbook in this organisation.
    /// </summary>
    Task<bool> DeleteLookbookAsync(
        Guid id,
        Guid orgId,
        CancellationToken cancellationToken = default);

    Task<SourcingRequestDto> CreateSourcingRequestAsync(
        CreateSourcingRequestDto dto,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourcingRequestDto>> GetSourcingRequestsByOrgIdAsync(
        Guid orgId,
        string? status = null,
        CancellationToken cancellationToken = default);

    Task<SourcingRequestDto?> UpdateSourcingRequestStatusAsync(
        Guid id,
        Guid orgId,
        string status,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupplierDto>> GetSuppliersByOrgIdAsync(
        Guid orgId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupplierCatalogItemDto>> GetSupplierCatalogAsync(
        Guid supplierId,
        Guid orgId,
        string? category = null,
        string? color = null,
        decimal? maxPrice = null,
        CancellationToken cancellationToken = default);

    Task<QrCodeResponseDto> GenerateItemQrDtoAsync(
        Guid orgId,
        Guid itemId,
        string format = "png",
        int size = 300,
        CancellationToken cancellationToken = default);

    Task<byte[]> GenerateItemQrBytesAsync(
        Guid orgId,
        Guid itemId,
        string format = "png",
        int size = 300,
        CancellationToken cancellationToken = default);

    QrCodeResponseDto GenerateQrResponse(GenerateQrDto dto);

    Task<QrScanResultDto> ScanAndResolveAsync(
        Guid orgId,
        ScanQrDto request,
        CancellationToken cancellationToken = default);

    Task<QrScanResultDto> ScanAndResolveImageBytesAsync(
        Guid orgId,
        byte[] imageBytes,
        CancellationToken cancellationToken = default);
}
