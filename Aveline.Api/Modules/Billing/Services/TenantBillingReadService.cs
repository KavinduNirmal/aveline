using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.DTOs;
using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// T5 · The two tenant billing reads the surface was missing: the billing-period history (E-12).
/// </summary>
/// <remarks>
/// A billing period is a <see cref="UsageAccount"/> row — the mutable Blossom account for one
/// period — enriched with:
///
/// 1. **its plan**, from the day's <c>OrganizationSubscriptionSnapshot</c> when one exists (the only
///    source that survives a plan change) and from the live subscription row otherwise. It is
///    deliberately **not** read from <c>Organization.PlanTier</c>: every organization has a tier, but
///    only some have ever had a billing row, and reporting the former as the latter would invent a
///    subscription;
/// 2. **its top-ups**, counted from the append-only Blossom ledger within the period. Read from the
///    ledger rather than from <c>UsageAccount.BlossomGranted</c> so a reader can see how many grants
///    there were, not only their sum — a single large grant and ten small ones are different facts;
/// 3. **its list price**, which is <c>null</c> whenever the stored `PriceLkr` is zero, with a flag
///    saying whether a price was configured at all (C-4). The column is never assigned anywhere in
///    the product, so a rendered `0` would be a fabrication rather than a free plan.
/// </remarks>
public static class TenantBillingReadService
{
    /// <summary>The page size the tenant Billing panel asks for when it does not specify one.</summary>
    public const int DefaultTake = 12;

    public const int MinTake = 1;

    /// <summary>Two years of history. A caller is told the limit rather than silently clamped.</summary>
    public const int MaxTake = 24;

    /// <summary>
    /// The most recent billing periods for one organization, newest first. Every query filters
    /// <c>OrganizationId</c> explicitly: tenant isolation is a route value plus per-query filters,
    /// with no EF query filter to fall back on.
    /// </summary>
    public static async Task<IReadOnlyList<BillingPeriodDto>> GetPeriodsAsync(
        AppDbContext db, Guid organizationId, int take, CancellationToken ct = default)
    {
        var effectiveTake = Math.Clamp(take, MinTake, MaxTake);

        var accounts = await db.UsageAccounts.AsNoTracking()
            .Where(account => account.OrganizationId == organizationId)
            .OrderByDescending(account => account.PeriodStart)
            .Take(effectiveTake)
            .ToListAsync(ct);

        if (accounts.Count == 0)
        {
            return Array.Empty<BillingPeriodDto>();
        }

        var periodStarts = accounts.Select(account => account.PeriodStart).ToList();

        // The snapshot is authoritative for a historical period; the subscription row (one current
        // row per organization) can only ever answer for the period it is currently in.
        var snapshots = await db.OrganizationSubscriptionSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.OrganizationId == organizationId
                               && periodStarts.Contains(snapshot.SnapshotDay))
            .ToListAsync(ct);
        var snapshotByDay = snapshots
            .GroupBy(snapshot => snapshot.SnapshotDay)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(s => s.CreatedAt).First());

        var subscriptions = await db.OrganizationSubscriptions.AsNoTracking()
            .Where(subscription => subscription.OrganizationId == organizationId
                                   && periodStarts.Contains(subscription.CurrentPeriodStart))
            .ToListAsync(ct);
        var subscriptionByStart = subscriptions
            .GroupBy(subscription => subscription.CurrentPeriodStart)
            .ToDictionary(group => group.Key, group => group.First());

        // Every top-up that landed in the window these periods cover, in one round trip.
        var rangeStart = accounts.Min(account => account.PeriodStart);
        var rangeEnd = accounts.Max(account => account.PeriodEnd);
        var topUps = await db.BlossomLedgerEntries.AsNoTracking()
            .Where(entry => entry.OrganizationId == organizationId
                            && entry.EntryType == BlossomLedgerEntryType.TopUpGrant
                            && entry.CreatedAt >= rangeStart
                            && entry.CreatedAt < rangeEnd)
            .Select(entry => new { entry.CreatedAt, entry.BlossomDelta })
            .ToListAsync(ct);

        var periods = new List<BillingPeriodDto>(accounts.Count);
        foreach (var account in accounts)
        {
            snapshotByDay.TryGetValue(account.PeriodStart, out var snapshot);
            subscriptionByStart.TryGetValue(account.PeriodStart, out var subscription);

            var hasSubscriptionRow = snapshot?.HasBillingRow ?? subscription is not null;

            string? planTier = null;
            decimal? planListPriceLkr = null;
            if (hasSubscriptionRow)
            {
                planTier = (snapshot?.PlanTier ?? subscription?.PlanTier)?.ToString();

                var price = snapshot?.PriceLkr ?? subscription?.PriceLkr ?? 0m;
                if (price > 0m)
                {
                    planListPriceLkr = price;
                }
            }

            var periodTopUps = topUps
                .Where(entry => entry.CreatedAt >= account.PeriodStart
                                && entry.CreatedAt < account.PeriodEnd)
                .ToList();

            periods.Add(new BillingPeriodDto(
                account.PeriodStart,
                account.PeriodEnd,
                account.IsClosed,
                planTier,
                hasSubscriptionRow,
                account.MonthlyBlossomLimit,
                account.BlossomGranted,
                account.BlossomAdjusted,
                account.BlossomUsed,
                account.BlossomRemaining,
                planListPriceLkr,
                planListPriceLkr.HasValue,
                periodTopUps.Sum(entry => entry.BlossomDelta),
                periodTopUps.Count));
        }

        return periods;
    }
}
