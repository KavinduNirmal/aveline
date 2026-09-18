using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public class InventoryRepository : IInventoryRepository
{
    private readonly AppDbContext _db;

    public InventoryRepository(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<InventoryItem?> GetByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default)
    {
        return await _db.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id && i.OrgId == orgId && i.DeletedAt == null, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryItem>> SearchAsync(
        Guid orgId,
        string? category = null,
        string? color = null,
        string? size = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        bool inStockOnly = true,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _db.InventoryItems
            .AsNoTracking()
            .Where(x =>
                x.OrgId == orgId &&
                x.DeletedAt == null &&
                x.Status == "available");

        if (!string.IsNullOrWhiteSpace(category))
        {
            var trimmedCategory = category.Trim().ToLower();
            query = query.Where(x => x.Category.ToLower().Contains(trimmedCategory));
        }

        if (!string.IsNullOrWhiteSpace(color))
        {
            var trimmedColor = color.Trim().ToLower();
            query = query.Where(x =>
                x.Color.ToLower().Contains(trimmedColor) ||
                (x.ItemName != null && x.ItemName.ToLower().Contains(trimmedColor)) ||
                (x.Description != null && x.Description.ToLower().Contains(trimmedColor)));
        }

        if (minPrice.HasValue)
        {
            query = query.Where(x => x.Price >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(x => x.Price <= maxPrice.Value);
        }

        if (inStockOnly)
        {
            query = query.Where(x => x.StockQuantity > 0);
        }

        var results = await query
            .OrderBy(x => x.ItemName)
            .ToListAsync(cancellationToken);

        // Size filter evaluation in memory to support JSON array across all providers
        if (!string.IsNullOrWhiteSpace(size))
        {
            results = results
                .Where(x => x.Sizes != null && x.Sizes.Any(s => s.Equals(size, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        // Apply pagination
        var p = page > 0 ? page : 1;
        var ps = pageSize > 0 ? pageSize : 20;

        return results
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToList();
    }

    public async Task<IReadOnlyList<InventoryItem>> GetLowStockAsync(
        Guid orgId,
        int threshold = 5,
        CancellationToken cancellationToken = default)
    {
        var items = await _db.InventoryItems
            .AsNoTracking()
            .Where(x =>
                x.OrgId == orgId &&
                x.DeletedAt == null &&
                x.Status == "available" &&
                x.StockQuantity <= threshold)
            .OrderBy(x => x.StockQuantity)
            .ToListAsync(cancellationToken);

        return items;
    }

    public async Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await _db.InventoryItems.AddAsync(item, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(InventoryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.UpdatedAtUtc = DateTime.UtcNow;
        _db.InventoryItems.Update(item);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default)
    {
        var item = await _db.InventoryItems
            .FirstOrDefaultAsync(i => i.Id == id && i.OrgId == orgId, cancellationToken);

        if (item is not null)
        {
            item.DeletedAt = DateTime.UtcNow;
            _db.InventoryItems.Update(item);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<InventoryImage?> GetImageByIdAsync(Guid imageId, Guid orgId, CancellationToken cancellationToken = default)
    {
        return await _db.InventoryImages
            .AsNoTracking()
            .FirstOrDefaultAsync(img => img.Id == imageId && img.OrgId == orgId, cancellationToken);
    }

    public async Task AddImageAsync(InventoryImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        await _db.InventoryImages.AddAsync(image, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
