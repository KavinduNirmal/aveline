using Aveline.Api.Common.Media;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;

namespace Aveline.Api.Modules.VisualIntelligence.Services;

public class InventoryService : IInventoryService
{
    private readonly IInventoryRepository _repository;

    public InventoryService(IInventoryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
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

    public async Task<CatalogPagedResponse> QueryCatalogAsync(
        Guid orgId,
        CatalogQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validate status vocabulary if provided
        if (request.Statuses is not null && request.Statuses.Count > 0)
        {
            foreach (var status in request.Statuses)
            {
                if (!Constants.CatalogStatusVocabulary.IsValid(status))
                {
                    throw new ArgumentException($"Invalid status value: '{status}'.", nameof(request));
                }
            }
        }

        var (items, total) = await _repository.QueryAsync(orgId, request, cancellationToken);

        var page = request.Page > 0 ? request.Page : 1;
        var pageSize = request.PageSize switch
        {
            < 1 => 20,
            > 200 => 200,
            _ => request.PageSize
        };

        return new CatalogPagedResponse
        {
            Items = items.Select(InventoryItemDto.FromDomain).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
            GeneratedAt = DateTime.UtcNow
        };
    }

    public async Task<CatalogFacetsResponse> GetFacetsAsync(
        Guid orgId,
        CatalogQueryRequest? currentNarrowing = null,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetFacetsAsync(orgId, currentNarrowing, cancellationToken);
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
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var commaIdx = trimmed.IndexOf(',');
                if (commaIdx > 0)
                {
                    var mimePart = trimmed[5..commaIdx];
                    var contentType = MediaContentTypes.DefaultImage;
                    if (mimePart.Contains(';'))
                    {
                        contentType = MediaContentTypes.NormalizeImage(mimePart.Split(';')[0]);
                    }
                    var bytes = Convert.FromBase64String(trimmed[(commaIdx + 1)..]);
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

                    await _repository.AddImageAsync(imageRecord, cancellationToken);
                    return imageRecord.ImageUrl;
                }
            }
            catch
            {
                // Retain string if decoding fails
            }
        }

        return trimmed;
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
