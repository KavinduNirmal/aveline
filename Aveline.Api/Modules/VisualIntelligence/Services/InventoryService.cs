using Aveline.Api.Common.Media;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.VisualIntelligence.Services;

public class InventoryService : IInventoryService
{
    private readonly IInventoryRepository _repository;
    private readonly MediaOptions _mediaOptions;

    /// <summary>
    /// The media options are optional so a caller that constructs the service directly (most of
    /// the existing unit tests) keeps the documented default cap; the DI graph always supplies
    /// the bound <see cref="MediaOptions"/>.
    /// </summary>
    public InventoryService(IInventoryRepository repository, IOptions<MediaOptions>? mediaOptions = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _mediaOptions = mediaOptions?.Value ?? new MediaOptions();
    }

    public async Task<IReadOnlyList<InventoryItemDto>> SearchInventoryAsync(
        SearchInventoryDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = request.Page > 0 ? request.Page : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 20;

        var items = await _repository.SearchAsync(
            orgId: request.OrgId,
            category: request.Category?.Trim(),
            color: request.Color?.Trim(),
            size: request.Size?.Trim(),
            minPrice: request.MinPrice,
            maxPrice: request.MaximumPrice,
            inStockOnly: request.InStockOnly,
            page: page,
            pageSize: pageSize,
            cancellationToken: cancellationToken
        );

        return items.Select(InventoryItemDto.FromDomain).ToList();
    }

    public async Task<InventoryItemDto?> GetItemByIdAsync(
        Guid id,
        Guid orgId,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetByIdAsync(id, orgId, cancellationToken);
        return item is null ? null : InventoryItemDto.FromDomain(item);
    }

    public async Task<InventoryItemDto> CreateItemAsync(
        CreateInventoryItemDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var processedImageUrl = await ProcessImageUrlAsync(dto.ImageUrl, dto.OrgId, cancellationToken);

        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = dto.OrgId,
            ItemName = string.IsNullOrWhiteSpace(dto.ItemName) ? "Untitled Piece" : dto.ItemName.Trim(),
            Category = string.IsNullOrWhiteSpace(dto.Category) ? "General" : dto.Category.Trim(),
            Color = string.IsNullOrWhiteSpace(dto.Color) ? "Unspecified" : dto.Color.Trim(),
            Fabric = dto.Fabric?.Trim(),
            Style = dto.Style?.Trim(),
            Sizes = dto.Sizes ?? new List<string>(),
            Price = dto.Price,
            Cost = dto.Cost,
            Quantity = dto.Quantity,
            Status = string.IsNullOrWhiteSpace(dto.Status) ? "available" : dto.Status.Trim(),
            ImageUrl = processedImageUrl,
            Sku = dto.Sku?.Trim(),
            Description = dto.Description?.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        await _repository.AddAsync(item, cancellationToken);

        return InventoryItemDto.FromDomain(item);
    }

    public async Task<InventoryItemDto?> UpdateItemAsync(
        Guid id,
        UpdateInventoryItemDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var item = await _repository.GetByIdAsync(id, dto.OrgId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(dto.ItemName)) item.ItemName = dto.ItemName.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Category)) item.Category = dto.Category.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Color)) item.Color = dto.Color.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Fabric)) item.Fabric = dto.Fabric.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Style)) item.Style = dto.Style.Trim();
        if (dto.Sizes is not null) item.Sizes = dto.Sizes;
        if (dto.Price.HasValue) item.Price = dto.Price.Value;
        if (dto.Cost.HasValue) item.Cost = dto.Cost.Value;
        if (dto.Quantity.HasValue) item.Quantity = dto.Quantity.Value;
        if (!string.IsNullOrWhiteSpace(dto.Status)) item.Status = dto.Status.Trim();
        if (dto.ImageUrl is not null) item.ImageUrl = await ProcessImageUrlAsync(dto.ImageUrl, dto.OrgId, cancellationToken);
        if (dto.Sku is not null) item.Sku = dto.Sku.Trim();
        if (dto.Description is not null) item.Description = dto.Description.Trim();
        if (dto.Metadata is not null) item.Metadata = dto.Metadata;

        await _repository.UpdateAsync(item, cancellationToken);

        return InventoryItemDto.FromDomain(item);
    }

    private async Task<string?> ProcessImageUrlAsync(string? imageUrl, Guid orgId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        var trimmed = imageUrl.Trim();
        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var commaIdx = trimmed.IndexOf(',');
        if (commaIdx <= 0)
        {
            return trimmed;
        }

        var mimePart = trimmed[5..commaIdx];
        var contentType = MediaContentTypes.DefaultImage;
        if (mimePart.Contains(';'))
        {
            contentType = MediaContentTypes.NormalizeImage(mimePart.Split(';')[0]);
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(trimmed[(commaIdx + 1)..]);
        }
        catch
        {
            // Retain string if decoding fails. The decode is the only best-effort step here.
            return trimmed;
        }

        // The same catalog ceiling the upload handler applies, checked outside every catch so
        // an over-cap image is refused visibly rather than swallowed and reported as a
        // successful item write (strategy §3.4; plan U0.6). The write below stays best-effort.
        var catalogMaxFileBytes = _mediaOptions.CatalogMaxFileBytes;
        if (bytes.LongLength > catalogMaxFileBytes)
        {
            throw new CatalogImageTooLargeException(bytes.LongLength, catalogMaxFileBytes);
        }

        var imageId = Guid.NewGuid();
        var imageRecord = new InventoryImage
        {
            Id = imageId,
            OrgId = orgId,
            ImageData = bytes,
            ContentType = contentType,
            FileName = $"item_{DateTime.UtcNow.Ticks}.jpg",
            FileSizeBytes = bytes.Length,
            ImageUrl = $"/api/v1/orgs/{orgId}/catalog/images/{imageId}",
            CreatedAtUtc = DateTime.UtcNow
        };

        try
        {
            await _repository.AddImageAsync(imageRecord, cancellationToken);
            return imageRecord.ImageUrl;
        }
        catch
        {
            // Best-effort storage: if the image cannot be stored, keep the caller's string so
            // the item write still succeeds (the module's established discipline).
            return trimmed;
        }
    }

    public async Task<InventoryItemDto?> UpdateStatusAsync(
        Guid id,
        Guid orgId,
        string status,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetByIdAsync(id, orgId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        item.Status = status;
        await _repository.UpdateAsync(item, cancellationToken);

        return InventoryItemDto.FromDomain(item);
    }

    public async Task<IReadOnlyList<InventoryItemDto>> GetLowStockItemsAsync(
        Guid orgId,
        int threshold = 5,
        CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetLowStockAsync(orgId, threshold, cancellationToken);
        return items.Select(InventoryItemDto.FromDomain).ToList();
    }

    public async Task<bool> DeleteItemAsync(
        Guid id,
        Guid orgId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetByIdAsync(id, orgId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await _repository.DeleteAsync(id, orgId, cancellationToken);
        return true;
    }
}

/// <summary>
/// Thrown when a catalog image payload exceeds <see cref="MediaOptions.CatalogMaxFileBytes"/>
/// (strategy §3.4). It is a dedicated type so a catalog write route can translate the refusal
/// into its own <c>400 { error }</c> shape without catching unrelated failures, and so an
/// over-cap image can never be swallowed by the best-effort store in
/// <c>InventoryService.ProcessImageUrlAsync</c> and reported as a successful item write
/// (plan U0.6).
/// </summary>
public sealed class CatalogImageTooLargeException : InvalidOperationException
{
    public CatalogImageTooLargeException(long sizeBytes, long maxFileBytes)
        : base($"A catalog image may be at most {maxFileBytes} bytes; the payload was {sizeBytes} bytes.")
    {
        SizeBytes = sizeBytes;
        MaxFileBytes = maxFileBytes;
    }

    /// <summary>The refused payload's size, in bytes.</summary>
    public long SizeBytes { get; }

    /// <summary>The configured ceiling the payload exceeded, in bytes.</summary>
    public long MaxFileBytes { get; }
}
