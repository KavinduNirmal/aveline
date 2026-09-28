using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public class CatalogTagRepository : ICatalogTagRepository
{
    private readonly AppDbContext _db;

    private static readonly (string Slug, string Label, string? ColorHex, int SortOrder)[] DefaultTags =
    [
        ("bridal", "Bridal", "#D4AF37", 0),
        ("festive", "Festive", "#9E2A2B", 1),
        ("new-in", "New in", "#2D6A4F", 2),
        ("handloom", "Handloom", "#B56576", 3),
        ("raw-silk", "Raw silk", "#6D597A", 4),
        ("evening", "Evening", "#1D3557", 5),
        ("bestsellers", "Bestsellers", "#E76F51", 6),
        ("atelier-pick", "Atelier pick", "#0077B6", 7)
    ];

    public CatalogTagRepository(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<CatalogTag>> GetTagsByOrgAsync(
        Guid orgId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        await EnsureDefaultTagsAsync(orgId, cancellationToken);

        var query = _db.CatalogTags
            .AsNoTracking()
            .Include(t => t.ItemTags)
            .Where(t => t.OrgId == orgId);

        if (!includeArchived)
        {
            query = query.Where(t => !t.IsArchived);
        }

        return await query
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Label)
            .ToListAsync(cancellationToken);
    }

    public async Task<CatalogTag?> GetByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default)
    {
        return await _db.CatalogTags
            .AsNoTracking()
            .Include(t => t.ItemTags)
            .FirstOrDefaultAsync(t => t.Id == id && t.OrgId == orgId, cancellationToken);
    }

    public async Task<CatalogTag?> GetBySlugAsync(Guid orgId, string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var normalized = slug.Trim().ToLowerInvariant();

        return await _db.CatalogTags
            .AsNoTracking()
            .Include(t => t.ItemTags)
            .FirstOrDefaultAsync(t => t.OrgId == orgId && t.Slug.ToLower() == normalized, cancellationToken);
    }

    public async Task AddAsync(CatalogTag tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);
        tag.CreatedAtUtc = DateTime.UtcNow;
        tag.Slug = tag.Slug.Trim().ToLowerInvariant();
        await _db.CatalogTags.AddAsync(tag, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(CatalogTag tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);
        tag.UpdatedAtUtc = DateTime.UtcNow;
        _db.CatalogTags.Update(tag);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default)
    {
        var tag = await _db.CatalogTags.FirstOrDefaultAsync(t => t.Id == id && t.OrgId == orgId, cancellationToken);
        if (tag is not null)
        {
            _db.CatalogTags.Remove(tag);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<CatalogTag>> GetTagsForItemAsync(
        Guid orgId,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        return await _db.InventoryItemTags
            .AsNoTracking()
            .Where(it => it.OrgId == orgId && it.ItemId == itemId)
            .Select(it => it.Tag)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> AssignTagsToItemAsync(
        Guid orgId,
        Guid itemId,
        IEnumerable<string> tagSlugsOrIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagSlugsOrIds);

        var itemExists = await _db.InventoryItems
            .AnyAsync(i => i.Id == itemId && i.OrgId == orgId && i.DeletedAt == null, cancellationToken);

        if (!itemExists)
        {
            throw new InvalidOperationException($"Inventory item {itemId} not found in org {orgId}.");
        }

        var incomingList = tagSlugsOrIds
            .Select(t => t?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Remove existing associations
        var existingAssociations = await _db.InventoryItemTags
            .Where(it => it.OrgId == orgId && it.ItemId == itemId)
            .ToListAsync(cancellationToken);

        _db.InventoryItemTags.RemoveRange(existingAssociations);

        if (incomingList.Count == 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return Array.Empty<string>();
        }

        // Fetch existing tags in this org
        var existingOrgTags = await _db.CatalogTags
            .Where(t => t.OrgId == orgId)
            .ToListAsync(cancellationToken);

        var assignedSlugs = new List<string>();

        foreach (var tagInput in incomingList)
        {
            if (string.IsNullOrWhiteSpace(tagInput)) continue;

            // Check if matches by Guid Id or Slug
            var matchedTag = existingOrgTags.FirstOrDefault(t =>
                (Guid.TryParse(tagInput, out var parsedGuid) && t.Id == parsedGuid) ||
                string.Equals(t.Slug, tagInput, StringComparison.OrdinalIgnoreCase));

            if (matchedTag == null)
            {
                // Auto-create tag if not existing
                var slug = tagInput.ToLowerInvariant().Replace(' ', '-');
                var label = char.ToUpperInvariant(tagInput[0]) + (tagInput.Length > 1 ? tagInput[1..] : string.Empty);
                matchedTag = new CatalogTag
                {
                    Id = Guid.NewGuid(),
                    OrgId = orgId,
                    Slug = slug,
                    Label = label,
                    SortOrder = existingOrgTags.Count,
                    CreatedAtUtc = DateTime.UtcNow
                };
                await _db.CatalogTags.AddAsync(matchedTag, cancellationToken);
                existingOrgTags.Add(matchedTag);
            }

            var itemTag = new InventoryItemTag
            {
                ItemId = itemId,
                TagId = matchedTag.Id,
                OrgId = orgId
            };
            await _db.InventoryItemTags.AddAsync(itemTag, cancellationToken);
            assignedSlugs.Add(matchedTag.Slug);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return assignedSlugs;
    }

    public async Task EnsureDefaultTagsAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        var hasTags = await _db.CatalogTags.AnyAsync(t => t.OrgId == orgId, cancellationToken);
        if (!hasTags)
        {
            foreach (var (slug, label, colorHex, sortOrder) in DefaultTags)
            {
                _db.CatalogTags.Add(new CatalogTag
                {
                    Id = Guid.NewGuid(),
                    OrgId = orgId,
                    Slug = slug,
                    Label = label,
                    ColorHex = colorHex,
                    SortOrder = sortOrder,
                    IsArchived = false,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
