using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Aveline.Api.Modules.Commerce.Services;

/// <summary>
/// The tenant dashboard's KPI aggregate (E-1) and the reduced takings read (E-13).
/// </summary>
/// <remarks>
/// **Server-side aggregation is not a preference.** The only aggregations in `Modules/Commerce` were
/// `CountAsync` calls for paging, and `GET …/customers` defaults to a 200-row page while orders page
/// at 20 — so summing pages in the browser would be O(rows) and could not produce a correct total for
/// a window it never fetched.
/// </remarks>
public interface ITenantDashboardService
{
    Task<TenantDashboardSummaryDto> GetSummaryAsync(
        Guid organizationId, string window, CancellationToken cancellationToken = default);

    Task<TenantDashboardSummaryDto> GetSummaryForRangeAsync(
        Guid organizationId, DateTime from, DateTime to, string label,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The reduced takings read: two labelled figures for every boutique role, and nothing else.
    /// </summary>
    Task<TenantTakingsDto> GetTakingsAsync(
        Guid organizationId, string window, CancellationToken cancellationToken = default);

    Task<TenantRevenueSeriesDto> GetRevenueSeriesAsync(
        Guid organizationId, DateTime? from, DateTime? to, string bucket,
        CancellationToken cancellationToken = default);

    Task<TenantTopItemsDto> GetTopItemsAsync(
        Guid organizationId, string window, int limit,
        CancellationToken cancellationToken = default);
}

public sealed class TenantDashboardService(
    AppDbContext db,
    IDistributedCache? cache,
    ILogger<TenantDashboardService> logger) : ITenantDashboardService
{
    /// <summary>The window presets the shell offers.</summary>
    public static readonly IReadOnlySet<string> KnownWindows =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "7d", "30d", "90d", "mtd", "ytd" };

    /// <summary>
    /// The statuses a sale KPI **counts**. Everything the product can write that is not in
    /// <see cref="ExcludedOrderStatuses"/>.
    /// </summary>
    /// <remarks>
    /// `payment_expired` is counted on purpose: it is **not terminal**, because
    /// `OrderService.ValidTransitions` lets it return to `payment_requested`, so an order that merely
    /// ran out its payment request is still business rather than a dead row. `confirmed` and
    /// `revised` are counted because the approval path writes them and the model's own comment
    /// forgets they exist.
    /// </remarks>
    public static readonly IReadOnlySet<string> CountedOrderStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pending_hold", "pending_approval", "approved", "confirmed", "revised",
            "payment_requested", "payment_expired", "payment_confirmed",
            "delivery_scheduled", "delivered", "completed",
        };

    /// <summary>
    /// The statuses a sale KPI **excludes**: the terminal-negative ones, the three with no outgoing
    /// edge that represent business that did not happen.
    /// </summary>
    public static readonly IReadOnlySet<string> ExcludedOrderStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cancelled", "rejected",
        };

    /// <summary>The low-stock threshold, matching the Home feed's and the catalogue endpoint's default.</summary>
    public const int LowStockThreshold = 5;

    /// <summary>Days without a visit after which a client is no longer "active" (ADR-010).</summary>
    public const int InactivityThresholdDays = 90;

    public const int MaxWindowDays = 400;

    public const int DefaultSeriesCap = 92;

    public const int DefaultTopItems = 5;

    public const int MaxTopItems = 20;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    // ── E-1: the summary ─────────────────────────────────────────────────────────────────────────

    public async Task<TenantDashboardSummaryDto> GetSummaryAsync(
        Guid organizationId, string window, CancellationToken cancellationToken = default)
    {
        if (!KnownWindows.Contains(window))
        {
            return TenantDashboardSummaryDto.Invalid(
                $"Unknown window '{window}'. Known windows: {string.Join(", ", KnownWindows)}.");
        }

        var (from, to) = ResolvePreset(window);
        return await GetSummaryForRangeAsync(organizationId, from, to, window, cancellationToken);
    }

    public async Task<TenantDashboardSummaryDto> GetSummaryForRangeAsync(
        Guid organizationId, DateTime from, DateTime to, string label,
        CancellationToken cancellationToken = default)
    {
        if (from >= to)
        {
            return TenantDashboardSummaryDto.Invalid("The window's start must be before its end.");
        }

        if ((to - from).TotalDays > MaxWindowDays)
        {
            return TenantDashboardSummaryDto.Invalid(
                $"The window may not exceed {MaxWindowDays} days.");
        }

        var cacheKey = $"tenant:dashboard:summary:{organizationId}:{label}:{from:O}:{to:O}";
        var (cached, cacheDegraded) = await TryReadCacheAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var summary = await BuildSummaryAsync(organizationId, from, to, label, cancellationToken);
        if (cacheDegraded)
        {
            // Surfaced in the response, not only in the log: an operator reading the dashboard during
            // a Redis outage should be able to tell that the figures came straight from the database.
            summary = summary with
            {
                DataQuality = summary.DataQuality.WithNote(
                    "The cache was unreachable, so these figures were computed directly. They are "
                    + "current; only the caching is degraded."),
            };
        }

        // A cache write is best-effort: the figures are already computed, and an outage must not
        // turn a working request into a failed one.
        await TryWriteCacheAsync(cacheKey, summary, cancellationToken);

        return summary;
    }

    private async Task<TenantDashboardSummaryDto> BuildSummaryAsync(
        Guid organizationId, DateTime from, DateTime to, string label,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();

        var currency = await ResolveCurrencyAsync(organizationId, cancellationToken);
        var sales = await BuildSalesAsync(organizationId, from, to, cancellationToken);
        var cash = await BuildCashAsync(organizationId, from, to, cancellationToken);
        var customers = await BuildCustomersAsync(organizationId, from, to, cancellationToken);
        var catalog = await BuildCatalogAsync(organizationId, cancellationToken);
        var team = await BuildTeamAsync(organizationId, cancellationToken);
        var usage = await BuildUsageAsync(organizationId, cancellationToken);
        var operations = await BuildOperationsAsync(organizationId, cancellationToken);

        var paymentRowsPresent = await db.Payments
            .AsNoTracking()
            .AnyAsync(payment => payment.OrganizationId == organizationId, cancellationToken);
        if (!paymentRowsPresent)
        {
            notes.Add(
                "No payment rows exist for this boutique yet, so the cash figures come from counter "
                + "sales only. This is not a measurement of zero.");
        }

        if (!sales.MarginCostsComplete)
        {
            notes.Add(
                "At least one order carries a zero wholesale cost, so the margin is incomplete. That "
                + "cost is typed rather than read from the inventory record.");
        }

        var backfilled = await db.BoutiqueSaleEntries
            .AsNoTracking()
            .AnyAsync(entry => entry.OrganizationId == organizationId
                               && entry.Reason.StartsWith(BoutiqueIncomeReadService.ReconciliationReasonPrefix),
                cancellationToken);
        if (backfilled)
        {
            notes.Add("The income ledger contains repaired entries, so it begins at a date.");
        }

        notes.Add($"Order statuses counted: {string.Join(", ", CountedOrderStatuses)}; "
                  + $"excluded: {string.Join(", ", ExcludedOrderStatuses)}.");

        return new TenantDashboardSummaryDto(
            label, from, to, DateTime.UtcNow, currency,
            sales, cash, customers, catalog, team, usage, operations,
            new TenantDashboardDataQualityDto(
                PaymentRowsPresent: paymentRowsPresent,
                OrderCostsComplete: sales.MarginCostsComplete,
                IncomeLedgerBackfilled: backfilled,
                UsageMetricsAvailable: usage.MonthlyBlossomLimit is not null,
                RefundAndOutstandingExcludedFromCollected: true,
                CheckedAt: DateTime.UtcNow,
                Notes: notes));
    }

    private async Task<TenantSalesKpisDto> BuildSalesAsync(
        Guid organizationId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var excluded = ExcludedOrderStatuses.ToArray();
        var orders = await db.Orders
            .AsNoTracking()
            .Where(order => order.OrganizationId == organizationId
                            && order.CreatedAt >= from && order.CreatedAt < to
                            && !excluded.Contains(order.Status.ToLower()))
            .Select(order => new { order.Total, order.Subtotal, order.Discount, order.TotalCost })
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            // An average over no orders does not exist, so it is null rather than a divide-by-zero
            // rendered as zero.
            return TenantSalesKpisDto.Empty;
        }

        var gross = orders.Sum(order => order.Total);
        var cost = orders.Sum(order => order.TotalCost);
        var margin = gross - cost;
        var costsComplete = orders.All(order => order.TotalCost > 0m);

        return new TenantSalesKpisDto(
            GrossOrderValue: gross,
            OrderCount: orders.Count,
            AverageOrderValue: decimal.Round(gross / orders.Count, 2),
            DiscountTotal: orders.Sum(order => order.Discount),
            MarginAmount: margin,
            // A margin percentage against zero revenue does not exist.
            MarginPercent: gross == 0m ? null : decimal.Round(margin / gross, 4))
        {
            MarginCostsComplete = costsComplete,
        };
    }

    private async Task<TenantCashKpisDto> BuildCashAsync(
        Guid organizationId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var payments = await db.Payments
            .AsNoTracking()
            .Where(payment => payment.OrganizationId == organizationId
                              && ((payment.Status == "confirmed"
                                   && payment.ConfirmedAt >= from && payment.ConfirmedAt < to)
                                  || (payment.Status == "pending"
                                      && payment.CreatedAt >= from && payment.CreatedAt < to)
                                  || (payment.Status == "refunded"
                                      && payment.CreatedAt >= from && payment.CreatedAt < to)))
            .Select(payment => new { payment.Status, payment.Amount })
            .ToListAsync(cancellationToken);

        if (payments.Count == 0)
        {
            return TenantCashKpisDto.Empty;
        }

        var confirmed = payments.Where(p => p.Status == "confirmed").ToList();
        var pending = payments.Where(p => p.Status == "pending").ToList();
        var refunded = payments.Where(p => p.Status == "refunded").ToList();

        return new TenantCashKpisDto(
            // Three figures, stated separately, so an outstanding request is never presented as a
            // receivable and a refund is never netted invisibly into what was taken.
            Collected: confirmed.Count == 0 ? 0m : confirmed.Sum(p => p.Amount),
            Outstanding: pending.Count == 0 ? 0m : pending.Sum(p => p.Amount),
            Refunded: refunded.Count == 0 ? 0m : refunded.Sum(p => p.Amount),
            RefundCount: refunded.Count);
    }

    private async Task<TenantCustomerKpisDto> BuildCustomersAsync(
        Guid organizationId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var customers = await db.Customers
            .AsNoTracking()
            .Where(customer => customer.OrganizationId == organizationId
                               && customer.DeletedAt == null)
            .Select(customer => new
            {
                customer.CreatedAt,
                customer.VisitCount,
                customer.LastVisitAt,
            })
            .ToListAsync(cancellationToken);

        var activeSince = DateTime.UtcNow.AddDays(-InactivityThresholdDays);

        return new TenantCustomerKpisDto(
            ActiveCount: customers.Count(customer =>
                customer.LastVisitAt is not null && customer.LastVisitAt >= activeSince),
            TotalCount: customers.Count,
            NewInWindow: customers.Count(customer =>
                customer.CreatedAt >= from && customer.CreatedAt < to),
            RepeatCount: customers.Count(customer => customer.VisitCount >= 2),
            InactivityThresholdDays: InactivityThresholdDays);
    }

    private async Task<TenantCatalogKpisDto> BuildCatalogAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var items = await db.InventoryItems
            .AsNoTracking()
            .Where(item => item.OrgId == organizationId && item.DeletedAt == null)
            .Select(item => new { item.StockQuantity, item.Cost })
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return TenantCatalogKpisDto.Empty;
        }

        return new TenantCatalogKpisDto(
            ItemCount: items.Count,
            LowStockCount: items.Count(item =>
                item.StockQuantity > 0 && item.StockQuantity <= LowStockThreshold),
            OutOfStockCount: items.Count(item => item.StockQuantity == 0),
            // Labelled "at cost", never "retail value".
            StockValueAtCost: items.Sum(item => item.Cost * item.StockQuantity));
    }

    private async Task<TenantTeamKpisDto> BuildTeamAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var memberships = await db.OrganizationMemberships
            .AsNoTracking()
            .Where(membership => membership.OrganizationId == organizationId
                                 && membership.Status == Modules.Organizations.Models.MembershipStatus.Active)
            .Select(membership => membership.BoutiqueRole)
            .ToListAsync(cancellationToken);

        var pendingInvitations = await db.OrganizationInvitations
            .AsNoTracking()
            .CountAsync(invitation => invitation.OrganizationId == organizationId
                                      && invitation.AcceptedAt == null
                                      && invitation.RevokedAt == null
                                      && invitation.ExpiresAt > DateTime.UtcNow,
                cancellationToken);

        // The plan limit lives in the entitlements surface; this aggregate deliberately reports the
        // count it can measure and leaves the allowance to the panel that owns it.
        return new TenantTeamKpisDto(
            ActiveSeats: memberships.Count,
            AllowedSeats: 0,
            PendingInvitations: pendingInvitations,
            RoleBreakdown: memberships
                .GroupBy(role => role)
                .ToDictionary(group => group.Key, group => group.Count()));
    }

    private async Task<TenantUsageKpisDto> BuildUsageAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        // The Blossom position is a live projection read, so it is read rather than cached, and a
        // missing account means "not measured" rather than zero.
        var account = await db.UsageAccounts
            .AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId)
            .OrderByDescending(candidate => candidate.PeriodStart)
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return TenantUsageKpisDto.Empty;
        }

        var limit = account.MonthlyBlossomLimit;
        return new TenantUsageKpisDto(
            BlossomUsed: account.BlossomUsed,
            MonthlyBlossomLimit: limit,
            BlossomRemaining: account.BlossomRemaining,
            PercentUsed: limit > 0m
                ? decimal.Round(account.BlossomUsed / limit * 100m, 1)
                : null);
    }

    private async Task<TenantOperationsKpisDto> BuildOperationsAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        // Each count reuses the predicate its owning module already uses, so two screens cannot
        // disagree about the same number.
        var pendingApprovals = await db.ApprovalQueue
            .AsNoTracking()
            .CountAsync(entry => entry.OrganizationId == organizationId
                                 && entry.Status.ToLower() == "pending", cancellationToken);

        var openConversations = await db.Conversations
            .AsNoTracking()
            .CountAsync(conversation => conversation.OrganizationId == organizationId
                                        && conversation.Status != Modules.Conversations.Models.ConversationStatus.Resolved
                                        && conversation.Status != Modules.Conversations.Models.ConversationStatus.Archived,
                cancellationToken);

        var scheduledDeliveries = await db.DeliveryPlans
            .AsNoTracking()
            .CountAsync(plan => plan.OrganizationId == organizationId
                                && plan.Status != "delivered" && plan.Status != "cancelled",
                cancellationToken);

        return new TenantOperationsKpisDto(pendingApprovals, openConversations, scheduledDeliveries);
    }

    // ── E-13: the reduced takings read ───────────────────────────────────────────────────────────

    public async Task<TenantTakingsDto> GetTakingsAsync(
        Guid organizationId, string window, CancellationToken cancellationToken = default)
    {
        if (!KnownWindows.Contains(window))
        {
            return TenantTakingsDto.Invalid(
                $"Unknown window '{window}'. Known windows: {string.Join(", ", KnownWindows)}.");
        }

        var (from, to) = ResolvePreset(window);

        // Copied from the ledger readers so the reduced card and the register cannot disagree.
        var entries = await db.BoutiqueSaleEntries
            .AsNoTracking()
            .Where(entry => entry.OrganizationId == organizationId
                            && entry.OccurredAt >= from && entry.OccurredAt < to
                            && entry.Status == BoutiqueSaleEntryStatus.Recorded)
            .Select(entry => new { entry.Kind, entry.ChargeBasis, entry.Amount })
            .ToListAsync(cancellationToken);

        var verified = entries
            .Where(entry => entry.ChargeBasis == BoutiqueSaleChargeBasis.Verified
                            && entry.Kind != BoutiqueSaleEntryKind.Refund)
            .Sum(entry => entry.Amount);
        var refunds = entries
            .Where(entry => entry.Kind == BoutiqueSaleEntryKind.Refund)
            .Sum(entry => entry.Amount);
        var derived = entries
            .Where(entry => entry.ChargeBasis == BoutiqueSaleChargeBasis.Derived)
            .Sum(entry => entry.Amount);

        var paymentRowsPresent = await db.Payments
            .AsNoTracking()
            .AnyAsync(payment => payment.OrganizationId == organizationId, cancellationToken);
        var backfilled = await db.BoutiqueSaleEntries
            .AsNoTracking()
            .AnyAsync(entry => entry.OrganizationId == organizationId
                               && entry.Reason.StartsWith(BoutiqueIncomeReadService.ReconciliationReasonPrefix),
                cancellationToken);

        var notes = new List<string>
        {
            "Collected is money taken, net of refunds. Billed, unconfirmed is what the orders say "
            + "was sold, with no evidence that money moved. They are never added together.",
        };
        if (!paymentRowsPresent)
        {
            notes.Add("No payment rows exist for this boutique yet, so the figures come from counter sales only.");
        }

        return new TenantTakingsDto(
            window, from, to, DateTime.UtcNow,
            await ResolveCurrencyAsync(organizationId, cancellationToken),
            // The two figures every role may see, and nothing else.
            Collected: verified - refunds,
            BilledUnconfirmed: derived,
            PaymentRowsPresent: paymentRowsPresent,
            LedgerBackfilled: backfilled,
            DataQuality: new TenantDashboardDataQualityDto(
                PaymentRowsPresent: paymentRowsPresent,
                OrderCostsComplete: true,
                IncomeLedgerBackfilled: backfilled,
                UsageMetricsAvailable: false,
                RefundAndOutstandingExcludedFromCollected: true,
                CheckedAt: DateTime.UtcNow,
                Notes: notes));
    }

    // ── E-2 and E-3 ──────────────────────────────────────────────────────────────────────────────

    public async Task<TenantRevenueSeriesDto> GetRevenueSeriesAsync(
        Guid organizationId, DateTime? from, DateTime? to, string bucket,
        CancellationToken cancellationToken = default)
    {
        var bucketUnit = (bucket ?? "day").Trim().ToLowerInvariant();
        if (bucketUnit is not ("day" or "week" or "month"))
        {
            return TenantRevenueSeriesDto.Invalid(
                $"Unknown bucket '{bucket}'. Known buckets: day, week, month.");
        }

        var effectiveTo = to ?? DateTime.UtcNow;
        var effectiveFrom = from ?? effectiveTo.AddDays(-DefaultSeriesCap);
        if (effectiveFrom >= effectiveTo)
        {
            return TenantRevenueSeriesDto.Invalid("The window's start must be before its end.");
        }

        var capped = false;
        if ((effectiveTo - effectiveFrom).TotalDays > DefaultSeriesCap)
        {
            effectiveFrom = effectiveTo.AddDays(-DefaultSeriesCap);
            capped = true;
        }

        var excluded = ExcludedOrderStatuses.ToArray();
        var orders = await db.Orders
            .AsNoTracking()
            .Where(order => order.OrganizationId == organizationId
                            && order.CreatedAt >= effectiveFrom && order.CreatedAt < effectiveTo
                            && !excluded.Contains(order.Status.ToLower()))
            .Select(order => new { order.CreatedAt, order.Total })
            .ToListAsync(cancellationToken);

        var ledger = await db.BoutiqueSaleEntries
            .AsNoTracking()
            .Where(entry => entry.OrganizationId == organizationId
                            && entry.OccurredAt >= effectiveFrom && entry.OccurredAt < effectiveTo
                            && entry.Status == BoutiqueSaleEntryStatus.Recorded)
            .Select(entry => new { entry.OccurredAt, entry.Kind, entry.ChargeBasis, entry.Amount })
            .ToListAsync(cancellationToken);

        var starts = BucketStarts(effectiveFrom, effectiveTo, bucketUnit);
        var points = new List<TenantRevenueBucketDto>();
        for (var index = 0; index < starts.Count; index++)
        {
            var start = starts[index];
            var end = index + 1 < starts.Count ? starts[index + 1] : effectiveTo;

            var inBucket = orders.Where(order => order.CreatedAt >= start && order.CreatedAt < end).ToList();
            var ledgerInBucket = ledger.Where(entry => entry.OccurredAt >= start && entry.OccurredAt < end).ToList();

            // Verified and not a refund: the same definition the register and the takings card use,
            // so the three surfaces cannot disagree about "collected".
            var collected = ledgerInBucket
                .Where(entry => entry.ChargeBasis == BoutiqueSaleChargeBasis.Verified
                                && entry.Kind != BoutiqueSaleEntryKind.Refund)
                .Sum(entry => entry.Amount);
            var refunded = ledgerInBucket
                .Where(entry => entry.Kind == BoutiqueSaleEntryKind.Refund)
                .Sum(entry => entry.Amount);

            points.Add(new TenantRevenueBucketDto(
                start,
                // A bucket with no orders is `null`, not zero: the chart must render a gap as a gap.
                GrossOrderValue: inBucket.Count == 0 ? null : inBucket.Sum(order => order.Total),
                Collected: ledgerInBucket.Count == 0 ? null : collected - refunded,
                Refunded: ledgerInBucket.Count == 0 ? null : refunded,
                IsPartial: start == starts[0] && effectiveFrom > starts[0]
                           || index == starts.Count - 1 && end > effectiveTo));
        }

        return new TenantRevenueSeriesDto(
            bucketUnit, effectiveFrom, effectiveTo, capped, points);
    }

    public async Task<TenantTopItemsDto> GetTopItemsAsync(
        Guid organizationId, string window, int limit,
        CancellationToken cancellationToken = default)
    {
        if (!KnownWindows.Contains(window))
        {
            return TenantTopItemsDto.Invalid(
                $"Unknown window '{window}'. Known windows: {string.Join(", ", KnownWindows)}.");
        }

        var clamped = Math.Clamp(limit <= 0 ? DefaultTopItems : limit, 1, MaxTopItems);
        var (from, to) = ResolvePreset(window);
        var excluded = ExcludedOrderStatuses.ToArray();

        var rows = await db.OrderItems
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .Join(
                db.Orders.AsNoTracking().Where(order =>
                    order.OrganizationId == organizationId
                    && order.CreatedAt >= from && order.CreatedAt < to
                    && !excluded.Contains(order.Status.ToLower())),
                item => item.OrderId,
                order => order.Id,
                (item, order) => new { item.ItemName, item.Quantity, item.TotalPrice })
            .ToListAsync(cancellationToken);

        var items = rows
            // Grouped on the denormalised `ItemName`, because `OrderItem.ItemId` has no enforced link
            // to the catalogue: a renamed piece appears under both names rather than being silently
            // merged into one.
            .GroupBy(row => row.ItemName)
            .Select(group => new TenantTopItemDto(
                group.Key,
                group.Sum(row => row.Quantity),
                group.Sum(row => row.TotalPrice)))
            .OrderByDescending(item => item.Revenue)
            .Take(clamped)
            .ToArray();

        return new TenantTopItemsDto(from, to, clamped, items);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static (DateTime From, DateTime To) ResolvePreset(string window)
    {
        var now = DateTime.UtcNow;
        return window.ToLowerInvariant() switch
        {
            "7d" => (now.AddDays(-7), now),
            "90d" => (now.AddDays(-90), now),
            "mtd" => (new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), now),
            "ytd" => (new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), now),
            _ => (now.AddDays(-30), now),
        };
    }

    private static List<DateTime> BucketStarts(DateTime from, DateTime to, string bucket)
    {
        var starts = new List<DateTime>();
        var cursor = bucket switch
        {
            "week" => from.Date.AddDays(-(int)from.DayOfWeek),
            "month" => new DateTime(from.Year, from.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => from.Date,
        };

        while (cursor < to)
        {
            starts.Add(cursor);
            cursor = bucket switch
            {
                "week" => cursor.AddDays(7),
                "month" => cursor.AddMonths(1),
                _ => cursor.AddDays(1),
            };
        }

        // A degenerate window still yields one bucket, so the client's axis is never empty while the
        // server sent a series.
        if (starts.Count == 0)
        {
            starts.Add(from);
        }

        return starts;
    }

    private async Task<string> ResolveCurrencyAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        // `Organization.Currency` is a real column, so it is read rather than hardcoded.
        var currency = await db.Organizations
            .AsNoTracking()
            .Where(org => org.Id == organizationId)
            .Select(org => org.Currency)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(currency) ? BoutiqueSaleLedgerService.DefaultCurrency : currency;
    }

    /// <summary>
    /// Reads the cached summary. The second value is `true` when the cache **failed** rather than
    /// missed, so the caller can report the degradation instead of implying a cache hit.
    /// </summary>
    private async Task<(TenantDashboardSummaryDto? Summary, bool Degraded)> TryReadCacheAsync(
        string key, CancellationToken cancellationToken)
    {
        if (cache is null)
        {
            return (null, false);
        }

        try
        {
            var bytes = await cache.GetAsync(key, cancellationToken);
            return bytes is null
                ? (null, false)
                : (JsonSerializer.Deserialize<TenantDashboardSummaryDto>(bytes), false);
        }
        catch (Exception exception)
        {
            // A cache outage degrades to an uncached query; it never fails the request.
            logger.LogWarning(exception, "Tenant dashboard cache read failed; degrading to an uncached query.");
            return (null, true);
        }
    }

    private async Task TryWriteCacheAsync(
        string key, TenantDashboardSummaryDto summary, CancellationToken cancellationToken)
    {
        if (cache is null)
        {
            return;
        }

        try
        {
            await cache.SetAsync(
                key,
                JsonSerializer.SerializeToUtf8Bytes(summary),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                cancellationToken);
        }
        catch (Exception exception)
        {
            // Swallowed for the same reason as the read: the figures are already computed.
            logger.LogWarning(exception, "Tenant dashboard cache write failed; the response is unaffected.");
        }
    }
}
