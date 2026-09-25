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
            .Include(i => i.ItemTags)
            .ThenInclude(it => it.Tag)
            .FirstOrDefaultAsync(i => i.Id == id && i.OrgId == orgId && i.DeletedAt == null, cancellationToken);
    }

    public async Task<InventoryItem?> GetBySkuAsync(string sku, Guid orgId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sku)) return null;
        var trimmed = sku.Trim().ToLower();
        return await _db.InventoryItems
            .AsNoTracking()
            .Include(i => i.ItemTags)
            .ThenInclude(it => it.Tag)
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
        CancellationToken cancellationToken = default)
    {
        var query = _db.InventoryItems
            .AsNoTracking()
            .Include(x => x.ItemTags)
            .ThenInclude(it => it.Tag)
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

    public async Task<(IReadOnlyList<InventoryItem> Items, int Total)> QueryAsync(
        Guid orgId,
        DTOs.CatalogQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = BuildBaseQuery(orgId, request);

        // Compute total count
        var total = await query.CountAsync(cancellationToken);

        // Sorting: default name:asc, deterministic fallback on Id
        var sortParts = (request.Sort ?? "name:asc").Trim().ToLower().Split(':');
        var sortField = sortParts[0];
        var isDesc = sortParts.Length > 1 && sortParts[1] == "desc";

        query = sortField switch
        {
            "price" => isDesc
                ? query.OrderByDescending(x => x.Price).ThenBy(x => x.Id)
                : query.OrderBy(x => x.Price).ThenBy(x => x.Id),
            "created" => isDesc
                ? query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
                : query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            "stock" => isDesc
                ? query.OrderByDescending(x => x.StockQuantity).ThenBy(x => x.Id)
                : query.OrderBy(x => x.StockQuantity).ThenBy(x => x.Id),
            _ => isDesc
                ? query.OrderByDescending(x => x.ItemName).ThenBy(x => x.Id)
                : query.OrderBy(x => x.ItemName).ThenBy(x => x.Id)
        };

        var page = request.Page > 0 ? request.Page : 1;
        var pageSize = request.PageSize switch
        {
            < 1 => 20,
            > 200 => 200,
            _ => request.PageSize
        };

        var items = await query
            .Include(x => x.ItemTags)
            .ThenInclude(it => it.Tag)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<DTOs.CatalogFacetsResponse> GetFacetsAsync(
        Guid orgId,
        DTOs.CatalogQueryRequest? currentNarrowing = null,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _db.InventoryItems
            .AsNoTracking()
            .Where(x => x.OrgId == orgId && x.DeletedAt == null);

        var totalItems = await baseQuery.CountAsync(cancellationToken);

        var response = new DTOs.CatalogFacetsResponse
        {
            GeneratedAt = DateTime.UtcNow,
            TotalItems = totalItems,
            Groups = new List<DTOs.CatalogFacetGroupDto>()
        };

        // 1. Availability (single-select in UI)
        // Group own selection does NOT constrain its own facet counts
        var queryWithoutStatus = BuildBaseQuery(orgId, currentNarrowing, ignoreStatuses: true);
        var statusCounts = await queryWithoutStatus
            .GroupBy(x => x.Status.ToLower())
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var statusMap = statusCounts.ToDictionary(k => k.Status, v => v.Count, StringComparer.OrdinalIgnoreCase);

        var availabilityGroup = new DTOs.CatalogFacetGroupDto
        {
            Key = "availability",
            Label = "Availability",
            SingleSelect = true,
            Options = new List<DTOs.CatalogFacetOptionDto>
            {
                new() { Value = "available", Label = "Available", Count = statusMap.GetValueOrDefault("available", 0) },
                new() { Value = "on_hold", Label = "On hold", Count = statusMap.GetValueOrDefault("on_hold", 0) },
                new() { Value = "unavailable", Label = "Unavailable", Count = statusMap.GetValueOrDefault("unavailable", 0) },
                new() { Value = "sold_out", Label = "Sold out", Count = statusMap.GetValueOrDefault("sold_out", 0) },
                new() { Value = "archived", Label = "Archived", Count = statusMap.GetValueOrDefault("archived", 0) }
            }
        };
        response.Groups.Add(availabilityGroup);

        // 2. Category
        var queryWithoutCategory = BuildBaseQuery(orgId, currentNarrowing, ignoreCategories: true);
        var categoryCounts = await queryWithoutCategory
            .Where(x => !string.IsNullOrWhiteSpace(x.Category))
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var standardCategories = new[] { "Sarees", "Lehengas", "Gowns", "Outerwear", "Silk blouses", "Accessories" };
        var catMap = categoryCounts.ToDictionary(k => k.Category, v => v.Count, StringComparer.OrdinalIgnoreCase);

        var categoryOptions = standardCategories.Select(cat => new DTOs.CatalogFacetOptionDto
        {
            Value = cat,
            Label = cat,
            Count = catMap.GetValueOrDefault(cat, 0)
        }).ToList();

        // Also add any other existing categories from DB
        foreach (var c in categoryCounts)
        {
            if (!standardCategories.Any(sc => sc.Equals(c.Category, StringComparison.OrdinalIgnoreCase)))
            {
                categoryOptions.Add(new DTOs.CatalogFacetOptionDto
                {
                    Value = c.Category,
                    Label = c.Category,
                    Count = c.Count
                });
            }
        }

        response.Groups.Add(new DTOs.CatalogFacetGroupDto
        {
            Key = "category",
            Label = "Category",
            SingleSelect = false,
            Options = categoryOptions
        });

        // 3. Fabric
        var queryWithoutFabric = BuildBaseQuery(orgId, currentNarrowing, ignoreFabrics: true);
        var fabricCounts = await queryWithoutFabric
            .Where(x => !string.IsNullOrWhiteSpace(x.Fabric))
            .GroupBy(x => x.Fabric!)
            .Select(g => new { Fabric = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var standardFabrics = new[] { "Raw silk", "Chiffon", "Handloom cotton", "Velvet", "Organza" };
        var fabricMap = fabricCounts.ToDictionary(k => k.Fabric, v => v.Count, StringComparer.OrdinalIgnoreCase);

        var fabricOptions = standardFabrics.Select(f => new DTOs.CatalogFacetOptionDto
        {
            Value = f,
            Label = f,
            Count = fabricMap.GetValueOrDefault(f, 0)
        }).ToList();

        foreach (var f in fabricCounts)
        {
            if (!standardFabrics.Any(sf => sf.Equals(f.Fabric, StringComparison.OrdinalIgnoreCase)))
            {
                fabricOptions.Add(new DTOs.CatalogFacetOptionDto
                {
                    Value = f.Fabric,
                    Label = f.Fabric,
                    Count = f.Count
                });
            }
        }

        response.Groups.Add(new DTOs.CatalogFacetGroupDto
        {
            Key = "fabric",
            Label = "Fabric",
            SingleSelect = false,
            Options = fabricOptions
        });

        // 4. Size
        var queryWithoutSize = BuildBaseQuery(orgId, currentNarrowing, ignoreSizes: true);
        var standardSizes = new[] { "36", "38", "40", "42", "Custom" };
        var allSizesInQuery = await queryWithoutSize
            .Select(x => x.Sizes)
            .ToListAsync(cancellationToken);

        var sizeOptions = standardSizes.Select(size => new DTOs.CatalogFacetOptionDto
        {
            Value = size,
            Label = size,
            Count = allSizesInQuery.Count(sizesList => sizesList != null && sizesList.Any(s => s.Equals(size, StringComparison.OrdinalIgnoreCase)))
        }).ToList();

        response.Groups.Add(new DTOs.CatalogFacetGroupDto
        {
            Key = "size",
            Label = "Size",
            SingleSelect = false,
            Options = sizeOptions
        });

        // 5. Price Bands
        var queryWithoutPrice = BuildBaseQuery(orgId, currentNarrowing, ignorePrice: true);
        var priceBands = new[]
        {
            new { Key = "under_25k", Label = "Under 25k", Min = (decimal?)null, Max = (decimal?)25000m, MaxInclusive = false },
            new { Key = "25k_to_75k", Label = "25k - 75k", Min = (decimal?)25000m, Max = (decimal?)75000m, MaxInclusive = false },
            new { Key = "75k_to_150k", Label = "75k - 150k", Min = (decimal?)75000m, Max = (decimal?)150000m, MaxInclusive = true },
            new { Key = "over_150k", Label = "Over 150k", Min = (decimal?)150000m, Max = (decimal?)null, MaxInclusive = false }
        };

        var priceOptions = new List<DTOs.CatalogFacetOptionDto>();
        foreach (var band in priceBands)
        {
            var q = queryWithoutPrice;
            if (band.Min.HasValue) q = q.Where(x => x.Price >= band.Min.Value);
            if (band.Max.HasValue)
            {
                q = band.MaxInclusive
                    ? q.Where(x => x.Price <= band.Max.Value)
                    : q.Where(x => x.Price < band.Max.Value);
            }

            var count = await q.CountAsync(cancellationToken);
            priceOptions.Add(new DTOs.CatalogFacetOptionDto
            {
                Value = band.Key,
                Label = band.Label,
                Count = count
            });
        }

        response.Groups.Add(new DTOs.CatalogFacetGroupDto
        {
            Key = "price",
            Label = "Price",
            SingleSelect = false,
            Options = priceOptions
        });

        return response;
    }

    private IQueryable<InventoryItem> BuildBaseQuery(
        Guid orgId,
        DTOs.CatalogQueryRequest? request,
        bool ignoreStatuses = false,
        bool ignoreCategories = false,
        bool ignoreFabrics = false,
        bool ignoreSizes = false,
        bool ignorePrice = false)
    {
        var query = _db.InventoryItems
            .AsNoTracking()
            .Where(x => x.OrgId == orgId && x.DeletedAt == null);

        if (request is null) return query;

        // Search text
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(x =>
                x.ItemName.ToLower().Contains(term) ||
                (x.Category != null && x.Category.ToLower().Contains(term)) ||
                (x.Description != null && x.Description.ToLower().Contains(term)) ||
                (x.Fabric != null && x.Fabric.ToLower().Contains(term)) ||
                (x.Sku != null && x.Sku.ToLower().Contains(term)));
        }

        // Status filter
        if (!ignoreStatuses)
        {
            if (request.Statuses is not null && request.Statuses.Count > 0)
            {
                var lowerStatuses = request.Statuses.Select(s => s.Trim().ToLower()).ToList();
                query = query.Where(x => lowerStatuses.Contains(x.Status.ToLower()));
            }
            else
            {
                query = query.Where(x => x.Status == "available");
            }
        }

        // Categories filter (multi-select union)
        if (!ignoreCategories && request.Categories is not null && request.Categories.Count > 0)
        {
            var lowerCategories = request.Categories.Select(c => c.Trim().ToLower()).ToList();
            query = query.Where(x => lowerCategories.Contains(x.Category.ToLower()));
        }

        // Fabrics filter (multi-select union)
        if (!ignoreFabrics && request.Fabrics is not null && request.Fabrics.Count > 0)
        {
            var lowerFabrics = request.Fabrics.Select(f => f.Trim().ToLower()).ToList();
            query = query.Where(x => x.Fabric != null && lowerFabrics.Contains(x.Fabric.ToLower()));
        }

        // Sizes filter (multi-select: matches if any size intersects)
        if (!ignoreSizes && request.Sizes is not null && request.Sizes.Count > 0)
        {
            // Build an OR expression across sizes using ILike pattern on the JSON serialized list
            var sizePredicates = request.Sizes.Select(s => $"%\"{s.Trim()}\"%").ToList();
            query = query.Where(x => sizePredicates.Any(p => EF.Functions.ILike(EF.Property<string>(x, "Sizes"), p)));
        }

        // Tags filter (multi-select: matches if any tag slug or tag Id matches)
        if (request.TagIds is not null && request.TagIds.Count > 0)
        {
            var lowerTags = request.TagIds
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLower())
                .ToList();

            if (lowerTags.Count > 0)
            {
                query = query.Where(item => item.ItemTags.Any(it =>
                    lowerTags.Contains(it.Tag.Slug.ToLower()) ||
                    lowerTags.Contains(it.TagId.ToString().ToLower())));
            }
        }

        // Price Bands and ad-hoc ranges
        if (!ignorePrice)
        {
            if (request.PriceBands is not null && request.PriceBands.Count > 0)
            {
                var bandPredicates = new List<System.Linq.Expressions.Expression<Func<InventoryItem, bool>>>();
                foreach (var b in request.PriceBands)
                {
                    var bandKey = b.Trim().ToLower();
                    switch (bandKey)
                    {
                        case "under_25k":
                            bandPredicates.Add(x => x.Price < 25000m);
                            break;
                        case "25k_to_75k":
                            bandPredicates.Add(x => x.Price >= 25000m && x.Price < 75000m);
                            break;
                        case "75k_to_150k":
                            bandPredicates.Add(x => x.Price >= 75000m && x.Price <= 150000m);
                            break;
                        case "over_150k":
                            bandPredicates.Add(x => x.Price > 150000m);
                            break;
                    }
                }

                if (bandPredicates.Count > 0)
                {
                    // Or across the band predicates
                    var parameter = System.Linq.Expressions.Expression.Parameter(typeof(InventoryItem), "x");
                    System.Linq.Expressions.Expression? combined = null;
                    foreach (var p in bandPredicates)
                    {
                        var invoked = System.Linq.Expressions.Expression.Invoke(p, parameter);
                        combined = combined == null ? invoked : System.Linq.Expressions.Expression.OrElse(combined, invoked);
                    }
                    if (combined != null)
                    {
                        var lambda = System.Linq.Expressions.Expression.Lambda<Func<InventoryItem, bool>>(combined, parameter);
                        query = query.Where(lambda);
                    }
                }
            }
            else
            {
                if (request.MinPrice.HasValue)
                {
                    query = query.Where(x => x.Price >= request.MinPrice.Value);
                }
                if (request.MaxPrice.HasValue)
                {
                    query = query.Where(x => x.Price <= request.MaxPrice.Value);
                }
            }
        }

        if (request.InStockOnly)
        {
            query = query.Where(x => x.StockQuantity > 0);
        }

        return query;
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
