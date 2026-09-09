using Aveline.Domain.Entities;
using Aveline.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Infrastructure.Persistence.Repositories;

public class InventoryRepository : IInventoryRepository
{
    private readonly AvelineDbContext _db;

    public InventoryRepository(AvelineDbContext db)
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
                x.Status == "available" &&
                (color == null || x.Color == color) &&
                (category == null || x.Category == category));

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
            query = query.Where(x => x.Quantity > 0);
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
                x.Quantity <= threshold)
            .OrderBy(x => x.Quantity)
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
}
