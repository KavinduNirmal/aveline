using Aveline.Application.DTOs.Inventory;
using Aveline.Domain.Repositories;

namespace Aveline.Application.Services.Inventory;

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

        var item = new Domain.Entities.InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = dto.OrgId,
            ItemName = dto.ItemName,
            Category = dto.Category,
            Color = dto.Color,
            Sizes = dto.Sizes ?? new List<string>(),
            Price = dto.Price,
            Quantity = dto.Quantity,
            Status = string.IsNullOrWhiteSpace(dto.Status) ? "available" : dto.Status,
            ImageUrl = dto.ImageUrl,
            Sku = dto.Sku,
            Description = dto.Description,
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

        if (!string.IsNullOrWhiteSpace(dto.ItemName)) item.ItemName = dto.ItemName;
        if (!string.IsNullOrWhiteSpace(dto.Category)) item.Category = dto.Category;
        if (!string.IsNullOrWhiteSpace(dto.Color)) item.Color = dto.Color;
        if (dto.Sizes is not null) item.Sizes = dto.Sizes;
        if (dto.Price.HasValue) item.Price = dto.Price.Value;
        if (dto.Quantity.HasValue) item.Quantity = dto.Quantity.Value;
        if (!string.IsNullOrWhiteSpace(dto.Status)) item.Status = dto.Status;
        if (dto.ImageUrl is not null) item.ImageUrl = dto.ImageUrl;
        if (dto.Sku is not null) item.Sku = dto.Sku;
        if (dto.Description is not null) item.Description = dto.Description;
        if (dto.Metadata is not null) item.Metadata = dto.Metadata;

        await _repository.UpdateAsync(item, cancellationToken);

        return InventoryItemDto.FromDomain(item);
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
}
