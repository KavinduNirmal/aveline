using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Revenue.Domain;
using Aveline.Api.Modules.Revenue.DTOs;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Revenue.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Revenue.Services;

/// <summary>
/// The revenue reads (S-50…S-55). Queries <see cref="AppDbContext"/> directly rather than through
/// repositories, following <c>BillingStatisticsService</c>: these are aggregate reads whose shape is
/// the query, and interposing a repository would only rename it.
/// </summary>
public sealed class RevenueStatisticsService(
    AppDbContext db,
    IOptions<RevenueOptions> options,
    IOptions<PaymentsOptions> payments,
    ILogger<RevenueStatisticsService> logger) : IRevenueStatisticsService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    private readonly RevenueOptions _options = options.Value;

    /// <summary>
    /// The payment configuration is what decides <c>revenueProviderSettlementAvailable</c>: a
    /// <c>manual</c> provider settles nothing by itself, so no figure is collected money.
    /// </summary>
    private readonly PaymentsOptions _payments = payments.Value;

    // ── S-50: the ledger register ───────────────────────────────────────────────────────────

    public async Task<RevenueResult<IncomeLedgerPageDto>> GetLedgerAsync(
        RevenueLedgerQuery query, CancellationToken cancellationToken = default)
    {
        if (!Validate(query.Window, RevenueWindowValidation.Day, out var window, out var message))
        {
            return RevenueResult<IncomeLedgerPageDto>.Invalid(message!);
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? DefaultPageSize : query.PageSize, 1, MaxPageSize);

        var inWindow = db.IncomeLedgerEntries.Where(entry =>
            entry.OccurredAt >= window!.From && entry.OccurredAt < window.To);

        var total = await inWindow.CountAsync(cancellationToken);
        var rows = await inWindow
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Totals are computed over the whole window, not the page, so the reconciliation describes
        // the period the caller asked about rather than the slice they are looking at.
        var all = await inWindow.ToListAsync(cancellationToken);
        var totals = RevenueReconciliation.Of(all);
        var quality = await BuildQualityAsync(all, cancellationToken);

        return RevenueResult<IncomeLedgerPageDto>.Ok(new IncomeLedgerPageDto(
            ToWindowDto(window!),
            rows.Select(IncomeLedgerEntryDto.From).ToArray(),
            total,
            page,
            pageSize,
            ToReconciliationDto(totals),
            quality));
    }

    // ── S-51: per-organization revenue ──────────────────────────────────────────────────────

    public async Task<RevenueResult<RevenueAccountsDto>> GetAccountsAsync(
        RevenueWindow window, CancellationToken cancellationToken = default)
    {
        if (!Validate(window, RevenueWindowValidation.Day, out var valid, out var message))
        {
            return RevenueResult<RevenueAccountsDto>.Invalid(message!);
        }

        var all = await InWindowAsync(valid!, cancellationToken);
        var names = await db.Organizations
            .ToDictionaryAsync(org => org.Id, org => new { org.Name, org.PlanTier }, cancellationToken);

        var items = all
            .GroupBy(entry => entry.OrganizationId)
            .Select(group =>
            {
                var totals = RevenueReconciliation.Of(group);
                names.TryGetValue(group.Key, out var organization);
                return new RevenueAccountItemDto(
                    group.Key,
                    organization?.Name ?? "Unknown organization",
                    organization?.PlanTier.ToString() ?? "Unknown",
                    totals.DerivedTotal,
                    totals.VerifiedTotal,
                    totals.RefundTotal,
                    totals.NetVerified,
                    totals.UnverifiedGap);
            })
            .OrderByDescending(item => item.DerivedTotal)
            .ToArray();

        return RevenueResult<RevenueAccountsDto>.Ok(new RevenueAccountsDto(
            ToWindowDto(valid!),
            items,
            items.Length,
            ToReconciliationDto(RevenueReconciliation.Of(all)),
            await BuildQualityAsync(all, cancellationToken)));
    }

    // ── S-52: MRR, ARR, ARPU ────────────────────────────────────────────────────────────────

    public async Task<RevenueResult<RevenueOverviewDto>> GetOverviewAsync(
        RevenueWindow window, CancellationToken cancellationToken = default)
    {
        // Snapshot-shaped, but still window-validated: the caller passes a window so the response is
        // bounded by the same cap as the series reads, rather than silently accepting any range.
        if (!Validate(window, RevenueWindowValidation.Day, out _, out var windowMessage))
        {
            return RevenueResult<RevenueOverviewDto>.Invalid(windowMessage!);
        }

        var subscriptions = await db.OrganizationSubscriptions
            .Where(subscription => subscription.Status == SubscriptionStatus.Active
                || subscription.Status == SubscriptionStatus.Trialing)
            .ToListAsync(cancellationToken);

        var priced = subscriptions.Where(subscription => subscription.PriceLkr > 0m).ToArray();
        var configured = subscriptions.Count == 0 || priced.Length > 0;

        // `null` rather than `0`: an unassigned price is not a free plan, and a `0` MRR would read
        // as "we earn nothing" instead of "the price is not configured".
        decimal? mrr = priced.Length == 0 ? null : priced.Sum(NormaliseToMonth);
        decimal? arr = mrr * 12m;
        var paying = priced.Select(subscription => subscription.OrganizationId).Distinct().Count();
        decimal? arpu = mrr is null || paying == 0 ? null : Math.Round(mrr.Value / paying, 2);

        var notes = new List<string>();
        if (!configured)
        {
            notes.Add(
                "No subscription has a list price configured, so MRR, ARR and ARPU are not "
                + "measured rather than zero.");
        }
        notes.Add(ProviderNote);

        return RevenueResult<RevenueOverviewDto>.Ok(new RevenueOverviewDto(
            DateTime.UtcNow,
            mrr,
            arr,
            arpu,
            paying,
            subscriptions.Count,
            new IncomeDataQualityDto(
                RevenueProviderSettlementAvailable: _payments.ProviderSettlesMoney,
                SubscriptionPricesConfigured: configured,
                DerivedEntriesUnverified: 0,
                CheckedAt: DateTime.UtcNow,
                Notes: notes)));
    }

    /// <summary>
    /// MRR is a monthly figure, so an annual row is divided by twelve. Without this a single annual
    /// subscription would appear to be twelve times its real monthly value.
    /// </summary>
    private static decimal NormaliseToMonth(OrganizationSubscription subscription) =>
        subscription.BillingCycle == BillingCycle.Annual
            ? subscription.PriceLkr / 12m
            : subscription.PriceLkr;

    // ── S-53: the timeseries ────────────────────────────────────────────────────────────────

    public async Task<RevenueResult<RevenueTimeseriesDto>> GetTimeseriesAsync(
        RevenueWindow window, string? granularity, CancellationToken cancellationToken = default)
    {
        if (!Validate(window, granularity, out var valid, out var message))
        {
            return RevenueResult<RevenueTimeseriesDto>.Invalid(message!);
        }

        var all = await InWindowAsync(valid!, cancellationToken);
        var points = BuildBuckets(valid!, (from, to) =>
        {
            var slice = all.Where(entry => entry.OccurredAt >= from && entry.OccurredAt < to).ToArray();
            var totals = RevenueReconciliation.Of(slice);
            return new RevenueTimeseriesPointDto(
                from,
                IsPartial(from, to, valid!),
                totals.DerivedTotal,
                totals.VerifiedTotal,
                totals.RefundTotal);
        });

        return RevenueResult<RevenueTimeseriesDto>.Ok(new RevenueTimeseriesDto(
            ToWindowDto(valid!),
            points,
            await BuildQualityAsync(all, cancellationToken)));
    }

    // ── S-54: collections ───────────────────────────────────────────────────────────────────

    public async Task<RevenueResult<RevenueCollectionsDto>> GetCollectionsAsync(
        RevenueWindow window, string? granularity, CancellationToken cancellationToken = default)
    {
        if (!Validate(window, granularity, out var valid, out var message))
        {
            return RevenueResult<RevenueCollectionsDto>.Invalid(message!);
        }

        var all = await InWindowAsync(valid!, cancellationToken);
        var points = BuildBuckets(valid!, (from, to) =>
        {
            var slice = all.Where(entry => entry.OccurredAt >= from && entry.OccurredAt < to).ToArray();
            var totals = RevenueReconciliation.Of(slice);
            return new RevenueCollectionPointDto(
                from,
                IsPartial(from, to, valid!),
                totals.DerivedTotal,
                totals.VerifiedTotal,
                totals.RefundTotal,
                // A window with nothing billed has no collection rate. `0` would be a different and
                // false claim: that something was billed and none of it was collected.
                totals.DerivedTotal == 0m
                    ? null
                    : Math.Round(totals.VerifiedTotal / totals.DerivedTotal * 100m, 2),
                // Signed, unlike the reconciliation's gap: "outstanding" is a balance, so a negative
                // value correctly says more came in than was billed. The gap is a magnitude because
                // it answers "how far apart are these", and a receipt with no charge is as much a
                // finding as a charge with no receipt.
                totals.DerivedTotal - totals.VerifiedTotal);
        });

        return RevenueResult<RevenueCollectionsDto>.Ok(new RevenueCollectionsDto(
            ToWindowDto(valid!),
            points,
            await BuildQualityAsync(all, cancellationToken)));
    }

    // ── S-55: Blossom pack sales ────────────────────────────────────────────────────────────

    public async Task<RevenueResult<RevenueBlossomSalesDto>> GetBlossomSalesAsync(
        RevenueWindow window, string? granularity, CancellationToken cancellationToken = default)
    {
        if (!Validate(window, granularity, out var valid, out var message))
        {
            return RevenueResult<RevenueBlossomSalesDto>.Invalid(message!);
        }

        var all = await InWindowAsync(valid!, cancellationToken);
        var sales = all.Where(entry => entry.Kind == IncomeEntryKind.TopUpPurchase).ToArray();

        var listPrice = sales
            .Where(entry => entry.ChargeBasis == IncomeChargeBasis.Derived)
            .Sum(entry => entry.Amount);
        var verified = sales
            .Where(entry => entry.ChargeBasis == IncomeChargeBasis.Verified)
            .Sum(entry => entry.Amount);

        // Blossoms come from the entitlement ledger, which is where the grant actually landed.
        var windows = sales
            .Where(entry => entry.SourceRef is not null)
            .Select(entry => entry.SourceRef!)
            .ToArray();
        var granted = await db.BlossomLedgerEntries
            .Where(entry => entry.EntryType == BlossomLedgerEntryType.TopUpGrant
                && entry.SourceRef != null
                && windows.Contains(entry.SourceRef))
            .SumAsync(entry => (decimal?)entry.BlossomDelta, cancellationToken) ?? 0m;

        // A top-up with no reference writes no income row at all, so it is invisible here by
        // construction. The grant total is reported alongside so the difference is visible.
        var grantedWithoutReference = await db.BlossomLedgerEntries
            .Where(entry => entry.EntryType == BlossomLedgerEntryType.TopUpGrant
                && (entry.SourceRef == null || entry.SourceRef == string.Empty))
            .CountAsync(cancellationToken);

        return RevenueResult<RevenueBlossomSalesDto>.Ok(new RevenueBlossomSalesDto(
            ToWindowDto(valid!),
            // A purchase writes a derived row, and verifying it writes a second row for the same
            // sale. Counting every row would double the pack count, so sales are counted by their
            // distinct dedup reference.
            sales.Select(entry => entry.SourceRef).Distinct().Count(),
            granted,
            listPrice,
            verified,
            listPrice == 0m ? null : Math.Round(verified / listPrice * 100m, 2),
            grantedWithoutReference,
            await BuildQualityAsync(all, cancellationToken)));
    }

    // ── Shared plumbing ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The prose behind <c>revenueProviderSettlementAvailable</c>, and it has to move with the flag:
    /// a console that still read "no payment provider" while a provider settled money would be
    /// lying in the opposite direction.
    /// </summary>
    private string ProviderNote => _payments.ProviderSettlesMoney
        ? $"A payment provider is configured ({_payments.Provider}) and settles charges, so a "
        + "Verified amount is collected money rather than an expectation. A Derived amount is still "
        + "only what a list price says should be billed."
        : "No payment provider is wired in this deployment, so no figure here is settled money: an "
        + "amount is either an expectation derived from a list price or a receipt an operator "
        + "confirmed.";

    private bool Validate(
        RevenueWindow window, string? granularity, out RevenueWindowValue? valid, out string? message)
    {
        var resolved = string.IsNullOrWhiteSpace(granularity) ? RevenueWindowValidation.Day : granularity;
        return RevenueWindowValidation.TryCreate(
            _options, window.From, window.To, resolved, out valid, out message);
    }

    private Task<List<IncomeLedgerEntry>> InWindowAsync(
        RevenueWindowValue window, CancellationToken cancellationToken) =>
        db.IncomeLedgerEntries
            .Where(entry => entry.OccurredAt >= window.From && entry.OccurredAt < window.To)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The dense bucket axis. Every bucket the window covers is present — a period with no rows is a
    /// real zero in a period that was observed, not a gap.
    /// </summary>
    private static IReadOnlyList<T> BuildBuckets<T>(
        RevenueWindowValue window, Func<DateTime, DateTime, T> project)
    {
        var points = new List<T>(window.BucketCount);
        foreach (var (start, end) in Buckets(window))
        {
            points.Add(project(start, end));
        }
        return points;
    }

    /// <summary>
    /// The window's buckets, on **calendar-aligned** boundaries.
    /// </summary>
    /// <remarks>
    /// Aligning to midnight rather than offsetting from `from` is what makes `isPartial` meaningful.
    /// A 06:00-to-18:00 window would otherwise produce buckets starting at 06:00, each exactly
    /// matching the window's own edge and therefore reported as complete — when in fact every one of
    /// them is clipped by the window and the first and last represent a fraction of a day.
    /// </remarks>
    private static IEnumerable<(DateTime Start, DateTime End)> Buckets(RevenueWindowValue window)
    {
        var cursor = Floor(window.From, window.Granularity);
        while (cursor < window.To)
        {
            var next = Next(cursor, window.Granularity);
            yield return (cursor, next);
            cursor = next;
        }
    }

    /// <summary>The calendar boundary at or before <paramref name="instant"/>.</summary>
    private static DateTime Floor(DateTime instant, string granularity)
    {
        var day = instant.Date;
        return granularity switch
        {
            RevenueWindowValidation.Month => new DateTime(day.Year, day.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            // ISO weeks start on Monday, which is what an operator reading a weekly series expects.
            RevenueWindowValidation.Week => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
            _ => DateTime.SpecifyKind(day, DateTimeKind.Utc),
        };
    }

    private static DateTime Next(DateTime start, string granularity) => granularity switch
    {
        RevenueWindowValidation.Month => start.AddMonths(1),
        RevenueWindowValidation.Week => start.AddDays(7),
        _ => start.AddDays(1),
    };

    /// <summary>
    /// True for a bucket the window clips: one whose start predates `from`, or whose end runs past
    /// `to`. A partial bucket covers less than a whole period, so its total is not comparable with a
    /// complete one's.
    /// </summary>
    private static bool IsPartial(DateTime from, DateTime to, RevenueWindowValue window) =>
        from < window.From || to > window.To;

    private async Task<IncomeDataQualityDto> BuildQualityAsync(
        IReadOnlyList<IncomeLedgerEntry> entries, CancellationToken cancellationToken)
    {
        // A derived row is "unverified" only when nothing live took it over. A row superseded by a
        // receipt is settled, so counting it would overstate the gap.
        var recorded = entries.Where(entry => entry.Status == IncomeEntryStatus.Recorded).ToArray();
        var verifiedIdentities = recorded
            .Where(entry => entry.ChargeBasis == IncomeChargeBasis.Verified && entry.SourceRef is not null)
            .Select(entry => (entry.SourceKind, entry.SourceRef))
            .ToHashSet();

        var unverified = recorded.Count(entry =>
            entry.ChargeBasis == IncomeChargeBasis.Derived
            && entry.SourceRef is not null
            && !verifiedIdentities.Contains((entry.SourceKind, entry.SourceRef)));

        var subscriptions = await db.OrganizationSubscriptions
            .Where(subscription => subscription.Status == SubscriptionStatus.Active
                || subscription.Status == SubscriptionStatus.Trialing)
            .Select(subscription => subscription.PriceLkr)
            .ToListAsync(cancellationToken);

        var configured = subscriptions.Count == 0 || subscriptions.Any(price => price > 0m);

        var notes = new List<string> { ProviderNote };
        if (!configured)
        {
            notes.Add(
                "No subscription has a list price configured, so MRR is not measured rather than "
                + "zero: an unassigned price is not a free plan.");
        }

        logger.LogDebug(
            "Revenue quality computed. unverified={Unverified} pricesConfigured={Configured}",
            unverified, configured);

        return new IncomeDataQualityDto(
            RevenueProviderSettlementAvailable: _payments.ProviderSettlesMoney,
            SubscriptionPricesConfigured: configured,
            DerivedEntriesUnverified: unverified,
            CheckedAt: DateTime.UtcNow,
            Notes: notes);
    }

    private static RevenueWindowDto ToWindowDto(RevenueWindowValue window) => new(
        window.From, window.To, window.Granularity, window.TimeZone, window.BucketCount);

    private static RevenueReconciliationDto ToReconciliationDto(RevenueReconciliationTotals totals) => new(
        totals.DerivedTotal,
        totals.VerifiedTotal,
        totals.UnverifiedGap,
        totals.RefundTotal,
        totals.NetVerified,
        totals.IsBalanced);
}
