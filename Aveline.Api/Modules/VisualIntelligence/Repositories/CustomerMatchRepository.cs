using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public class CustomerMatchRepository : ICustomerMatchRepository
{
    private readonly AppDbContext _db;

    public CustomerMatchRepository(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<CustomerMatch>> GetByItemIdAsync(Guid itemId, Guid orgId, double minScore = 0.7, CancellationToken cancellationToken = default)
    {
        var minConfidence = (decimal)minScore;
        return await _db.CustomerMatches
            .AsNoTracking()
            .Where(x => x.OrgId == orgId && x.ItemId == itemId && x.MatchConfidence >= minConfidence)
            .OrderByDescending(x => x.MatchConfidence)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerMatch>> GetByCustomerIdAsync(Guid customerId, Guid orgId, CancellationToken cancellationToken = default)
    {
        return await _db.CustomerMatches
            .AsNoTracking()
            .Where(x => x.OrgId == orgId && x.CustomerId == customerId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<CustomerMatch> matches, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matches);
        await _db.CustomerMatches.AddRangeAsync(matches, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerMatchDto>> GetEnrichedMatchesByItemIdAsync(Guid itemId, Guid orgId, double minScore = 0.7, CancellationToken cancellationToken = default)
    {
        var minConfidence = (decimal)minScore;
        var matches = await _db.CustomerMatches
            .AsNoTracking()
            .Where(x => x.OrgId == orgId && x.ItemId == itemId && x.MatchConfidence >= minConfidence)
            .OrderByDescending(x => x.MatchConfidence)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            return Array.Empty<CustomerMatchDto>();
        }

        var customerIds = matches.Select(m => m.CustomerId).Distinct().ToList();
        var customerMap = await _db.Customers
            .Include(c => c.Preferences)
            .AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var dtos = new List<CustomerMatchDto>();
        foreach (var m in matches)
        {
            customerMap.TryGetValue(m.CustomerId, out var customer);
            dtos.Add(new CustomerMatchDto
            {
                CustomerId = m.CustomerId,
                CustomerName = customer?.FullName ?? "Customer",
                CustomerPhone = customer?.PhoneNumber,
                MatchScore = (double)m.MatchConfidence,
                MatchReason = m.MatchReason,
                MatchingPreferences = customer?.Preferences?.Select(p => $"{p.PreferenceKey}: {p.PreferenceValue}").ToList() ?? new List<string>()
            });
        }

        return dtos;
    }

    public async Task<IReadOnlyList<CustomerMatchDto>> GenerateMatchesForInventoryItemAsync(Guid itemId, Guid orgId, int maxMatches = 10, CancellationToken cancellationToken = default)
    {
        var item = await _db.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == itemId && x.OrgId == orgId && x.DeletedAt == null, cancellationToken);

        var customers = await _db.Customers
            .Include(c => c.Preferences)
            .AsNoTracking()
            .Where(c => c.OrganizationId == orgId && c.Status != "deleted")
            .ToListAsync(cancellationToken);

        if (customers.Count == 0)
        {
            return Array.Empty<CustomerMatchDto>();
        }

        var candidateMatches = new List<CustomerMatchDto>();
        var itemCategory = item?.Category?.ToLowerInvariant() ?? string.Empty;
        var itemColor = item?.Color?.ToLowerInvariant() ?? string.Empty;
        var itemName = item?.ItemName?.ToLowerInvariant() ?? string.Empty;

        foreach (var customer in customers)
        {
            var matchingPrefs = new List<string>();
            decimal score = 0.50m; // Base affinity for boutique customer

            if (customer.Preferences != null)
            {
                foreach (var pref in customer.Preferences)
                {
                    var pKey = pref.PreferenceKey.ToLowerInvariant();
                    var pVal = pref.PreferenceValue.ToLowerInvariant();

                    if (!string.IsNullOrEmpty(itemColor) && (pVal.Contains(itemColor) || (!string.IsNullOrEmpty(pKey) && pKey.Contains("color") && itemColor.Contains(pVal))))
                    {
                        score += 0.25m;
                        matchingPrefs.Add($"Color: {pref.PreferenceValue}");
                    }

                    if (!string.IsNullOrEmpty(itemCategory) && (pVal.Contains(itemCategory) || pKey.Contains("category") || itemCategory.Contains(pVal)))
                    {
                        score += 0.20m;
                        matchingPrefs.Add($"Category: {pref.PreferenceValue}");
                    }

                    if (pVal.Contains("silk") || pVal.Contains("luxury") || pVal.Contains("formal") || pVal.Contains("wedding") || pVal.Contains("traditional"))
                    {
                        if (itemName.Contains(pVal) || itemCategory.Contains(pVal))
                        {
                            score += 0.15m;
                            matchingPrefs.Add($"Style: {pref.PreferenceValue}");
                        }
                    }
                }
            }

            if (customer.Status == "vip")
            {
                score += 0.10m;
            }
            else if (customer.Status == "returning")
            {
                score += 0.05m;
            }

            var clampedScore = Math.Min(0.98m, Math.Max(0.50m, score));
            var reason = matchingPrefs.Count > 0
                ? $"High affinity matching: {string.Join(", ", matchingPrefs)}"
                : $"Customer profile match based on boutique history (Status: {customer.Status})";

            candidateMatches.Add(new CustomerMatchDto
            {
                CustomerId = customer.Id,
                CustomerName = customer.FullName ?? "Customer",
                CustomerPhone = customer.PhoneNumber,
                MatchScore = (double)clampedScore,
                MatchReason = reason,
                MatchingPreferences = matchingPrefs
            });
        }

        var topMatches = candidateMatches
            .OrderByDescending(x => x.MatchScore)
            .Take(maxMatches > 0 ? maxMatches : 10)
            .ToList();

        // Persist generated matches into CustomerMatches table
        var newMatches = topMatches.Select(m => new CustomerMatch
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemId = itemId,
            CustomerId = m.CustomerId,
            MatchConfidence = (decimal)m.MatchScore,
            MatchReason = m.MatchReason,
            CreatedAtUtc = DateTime.UtcNow
        }).ToList();

        if (newMatches.Count > 0)
        {
            await _db.CustomerMatches.AddRangeAsync(newMatches, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return topMatches;
    }
}
