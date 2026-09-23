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

    public async Task<InventoryItem?> GetBySkuAsync(string sku, Guid orgId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sku)) return null;
        var trimmed = sku.Trim().ToLower();
        return await _db.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i =>
                i.OrgId == orgId &&
                i.DeletedAt == null &&
                i.Sku != null &&
                i.Sku.ToLower() == trimmed,
                cancellationToken);
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
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _db.InventoryItems
            .AsNoTracking()
            .Where(x =>
                x.OrgId == orgId &&
                x.DeletedAt == null &&
                x.Status == "available");

        if (!string.IsNullOrWhiteSpace(query))
        {
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "do", "we", "you", "u", "have", "any", "in", "stock", "is", "there", "a", "an", "the",
                "are", "show", "me", "find", "looking", "for", "please", "can", "i", "get", "what",
                "pieces", "available", "items", "some", "our"
            };
            var terms = query
                .Split(new[] { ' ', '?', '!', '.', ',', '"', '\'', ':', ';', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 1 && !stopWords.Contains(t))
                .Select(t => t.Trim().ToLower())
                .Distinct()
                .ToList();

            if (terms.Count > 0)
            {
                foreach (var term in terms)
                {
                    dbQuery = dbQuery.Where(x =>
                        (x.ItemName != null && x.ItemName.ToLower().Contains(term)) ||
                        (x.Category != null && x.Category.ToLower().Contains(term)) ||
                        (x.Description != null && x.Description.ToLower().Contains(term)) ||
                        (x.Fabric != null && x.Fabric.ToLower().Contains(term)) ||
                        (x.Style != null && x.Style.ToLower().Contains(term)) ||
                        (x.Color != null && x.Color.ToLower().Contains(term)));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var trimmedCategory = category.Trim().ToLower();
            dbQuery = dbQuery.Where(x => x.Category.ToLower().Contains(trimmedCategory));
        }

        if (minPrice.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.Price >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.Price <= maxPrice.Value);
        }

        if (inStockOnly)
        {
            dbQuery = dbQuery.Where(x => x.StockQuantity > 0);
        }

        var results = await dbQuery
            .OrderBy(x => x.ItemName)
            .ToListAsync(cancellationToken);

        // Color and size filter evaluation in memory to support complex fuzzy variants and JSON arrays across all providers
        if (!string.IsNullOrWhiteSpace(color))
        {
            var colors = color.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(c => c.Trim().ToLower())
                              .ToList();

            results = results.Where(x =>
                (x.Color != null && colors.Any(c => x.Color.ToLower().Contains(c))) ||
                (x.ItemName != null && colors.Any(c => 
                    x.ItemName.ToLower() == c || 
                    x.ItemName.ToLower().StartsWith(c + " ") || 
                    x.ItemName.ToLower().EndsWith(" " + c) || 
                    x.ItemName.ToLower().Contains(" " + c + " "))) ||
                (x.Description != null && colors.Any(c => 
                    x.Description.ToLower() == c || 
                    x.Description.ToLower().StartsWith(c + " ") || 
                    x.Description.ToLower().EndsWith(" " + c) || 
                    x.Description.ToLower().Contains(" " + c + " ")))
            ).ToList();
        }

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

    public async Task DeleteImageAsync(Guid imageId, Guid orgId, CancellationToken cancellationToken = default)
    {
        // Separate from DeleteAsync, which soft-deletes the item only and touches no image row.
        var image = await _db.InventoryImages
            .FirstOrDefaultAsync(img => img.Id == imageId && img.OrgId == orgId, cancellationToken);

        if (image is not null)
        {
            _db.InventoryImages.Remove(image);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
