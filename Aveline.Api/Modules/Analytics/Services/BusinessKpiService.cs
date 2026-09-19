using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Admin.Models;
using Aveline.Api.Modules.Analytics.DTOs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Analytics.Services;

/// <summary>
/// The business-KPI reads. Phase 2 delivers S-44 (growth), S-45 (active users) and S-46
/// (plan mix); phase 3 adds S-47…S-49.
/// </summary>
/// <remarks>
/// <para>
/// <b>Null versus zero, stated once.</b> A count of <c>0</c> in a bucket that lies inside the
/// observed period is honest and is emitted as <c>0</c>. A bucket before the earliest
/// observation, or a measure that could not be computed at all, is <c>null</c>.
/// </para>
/// <para>
/// <b>Bucketing happens in memory.</b> The measures come from different tables and rolling
/// day→week→month is a fold over at most <c>MaxWindowDays</c> (400) days of rows. Pushing
/// <c>date_trunc</c> into separate <c>GROUP BY</c> statements and then joining them is more
/// code and more fragile for the same result.
/// </para>
/// </remarks>
public sealed class BusinessKpiService(
    AppDbContext db,
    IClaimIdentityMap identities,
    TimeProvider timeProvider) : IBusinessKpiService
{
    private static readonly string[] OrderedTiers =
    [
        nameof(PlanTier.Seed),
        nameof(PlanTier.Bloom),
        nameof(PlanTier.Orchid),
        nameof(PlanTier.Rose),
        nameof(PlanTier.Enterprise),
    ];

    // ── S-44 growth ───────────────────────────────────────────────────────────────────────

    public async Task<BusinessGrowthDto> GetGrowthAsync(
        BusinessWindow window,
        string? cacheKey,
        CancellationToken cancellationToken = default)
    {
        var current = await LoadGrowthSourceAsync(window, cancellationToken);

        var previousFrom = window.From - (window.To - window.From);
        var previous = await LoadGrowthSourceAsync(
            new BusinessWindow(previousFrom, window.From, window.Granularity, window.BucketCount),
            cancellationToken);

        var axis = BusinessKpiValidation.BuildAxis(window.From, window.To, window.Granularity);
        var series = new List<GrowthPointDto>(axis.Count);
        var totals = GrowthTotalsDto.Empty;

        foreach (var bucketStart in axis)
        {
            var newUsers = SumForBucket(current.Users, bucketStart, window.Granularity);
            var newOrganizations = SumForBucket(current.Organizations, bucketStart, window.Granularity);
            var requests = SumForBucket(current.Requests, bucketStart, window.Granularity);
            var approved = SumForBucket(current.Approved, bucketStart, window.Granularity);

            series.Add(new GrowthPointDto(
                bucketStart,
                IsPartial(bucketStart, window),
                newUsers,
                newOrganizations,
                requests,
                approved));

            totals = totals with
            {
                NewUsers = totals.NewUsers + newUsers,
                NewOrganizations = totals.NewOrganizations + newOrganizations,
                NewAdminRequests = totals.NewAdminRequests + requests,
                ApprovedAdminRequests = totals.ApprovedAdminRequests + approved,
            };
        }

        var previousTotals = new GrowthTotalsDto(
            previous.Users.Sum(entry => entry.Value),
            previous.Organizations.Sum(entry => entry.Value),
            previous.Requests.Sum(entry => entry.Value),
            previous.Approved.Sum(entry => entry.Value));

        return new BusinessGrowthDto(
            ToDto(window),
            current.ObservedFrom,
            series,
            totals,
            previousTotals,
            DataQuality());
    }

    private sealed record GrowthSource(
        Dictionary<DateTime, int> Users,
        Dictionary<DateTime, int> Organizations,
        Dictionary<DateTime, int> Requests,
        Dictionary<DateTime, int> Approved,
        DateTime? ObservedFrom);

    private async Task<GrowthSource> LoadGrowthSourceAsync(
        BusinessWindow window,
        CancellationToken cancellationToken)
    {
        // Users.CreatedAt and Organizations.CreatedAt carry a time component, so the bucket key
        // is derived in memory rather than through `.Date` inside a GroupBy, which providers
        // translate inconsistently. The row ceiling is the window, never the table.
        var userRows = await db.Users
            .Where(user => user.CreatedAt >= window.From && user.CreatedAt < window.To && user.DeletedAt == null)
            .Select(user => user.CreatedAt)
            .ToListAsync(cancellationToken);

        var organizationRows = await db.Organizations
            .Where(organization => organization.CreatedAt >= window.From && organization.CreatedAt < window.To)
            .Select(organization => organization.CreatedAt)
            .ToListAsync(cancellationToken);

        var requestRows = await db.AdminApprovalRequests
            .Where(request => request.RequestedAt >= window.From && request.RequestedAt < window.To)
            .Select(request => new { request.RequestedAt, request.Status })
            .ToListAsync(cancellationToken);

        DateTime? observedFrom = null;
        foreach (var candidate in new[]
                 {
                     userRows.Count == 0 ? (DateTime?)null : userRows.Min(),
                     organizationRows.Count == 0 ? (DateTime?)null : organizationRows.Min(),
                     requestRows.Count == 0 ? (DateTime?)null : requestRows.Min(row => row.RequestedAt),
                 })
        {
            if (candidate is { } value && (observedFrom is null || value < observedFrom))
            {
                // A day boundary: the contract calls ObservedFrom "the earliest row date", so
                // the time component of the source instant is deliberately dropped.
                observedFrom = value.Date;
            }
        }

        return new GrowthSource(
            CountByBucket(userRows, window.Granularity),
            CountByBucket(organizationRows, window.Granularity),
            CountByBucket(requestRows.Select(row => row.RequestedAt), window.Granularity),
            CountByBucket(
                requestRows.Where(row => row.Status == AdminApprovalStatus.Approved).Select(row => row.RequestedAt),
                window.Granularity),
            observedFrom);
    }

    // ── S-45 active users ─────────────────────────────────────────────────────────────────

    public async Task<BusinessActiveUsersDto> GetActiveUsersAsync(
        BusinessWindow window,
        string? cacheKey,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.ApiRequestMetrics
            .Where(metric => metric.WindowSize == "day"
                             && metric.WindowStart >= window.From
                             && metric.WindowStart < window.To)
            .Select(metric => new { metric.WindowStart, metric.UserId, metric.OrganizationId })
            .ToListAsync(cancellationToken);

        var attributed = rows.Where(row => row.UserId is not null).ToList();
        var attributionAvailable = attributed.Count > 0;

        var usersByDay = attributed
            .GroupBy(row => row.WindowStart.Date)
            .ToDictionary(group => group.Key, group => group.Select(row => row.UserId!.Value).Distinct().Count());
        var organizationsByDay = attributed
            .GroupBy(row => row.WindowStart.Date)
            .ToDictionary(
                group => group.Key,
                group => group.Where(row => row.OrganizationId is not null)
                    .Select(row => row.OrganizationId!.Value)
                    .Distinct()
                    .Count());

        var axis = BusinessKpiValidation.BuildAxis(window.From, window.To, window.Granularity);
        var series = axis
            .Select(bucketStart => new ActiveUsersPointDto(
                bucketStart,
                IsPartial(bucketStart, window),
                attributionAvailable ? SumForBucket(usersByDay, bucketStart, window.Granularity) : null,
                attributionAvailable ? SumForBucket(organizationsByDay, bucketStart, window.Granularity) : null))
            .ToList();

        var dau = await DistinctUsersAsync(window.To.AddDays(-1), window.To, cancellationToken);
        var wau = await DistinctUsersAsync(window.To.AddDays(-7), window.To, cancellationToken);
        var mau = await DistinctUsersAsync(window.To.AddDays(-30), window.To, cancellationToken);

        double? stickiness = mau is > 0 && dau is { } daily
            ? (double)daily / mau.Value
            : null;

        var quality = DataQuality(
            userAttributionAvailable: attributionAvailable,
            notes: attributionAvailable
                ? null
                : "User attribution is unavailable for this window: no API request carried a resolved user id, "
                  + "so the active-user measures are reported as not measured rather than as zero. SignalR-only "
                  + "sessions are not counted by this definition.");

        return new BusinessActiveUsersDto(
            ToDto(window),
            series,
            new RollingActiveUsersDto(dau, wau, mau, stickiness),
            quality);
    }

    private async Task<int?> DistinctUsersAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var userIds = await db.ApiRequestMetrics
            .Where(metric => metric.WindowSize == "day"
                             && metric.WindowStart >= from
                             && metric.WindowStart < to
                             && metric.UserId != null)
            .Select(metric => metric.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        // No attributed rows means the reading is unavailable, not zero (R-1).
        return userIds.Count == 0 ? null : userIds.Count;
    }

    // ── S-46 plan mix ─────────────────────────────────────────────────────────────────────

    public async Task<BusinessPlanMixDto> GetPlanMixAsync(
        string? cacheKey,
        CancellationToken cancellationToken = default)
    {
        var organizationRows = await db.Organizations
            .Select(organization => new { organization.PlanTier, organization.IsActive })
            .ToListAsync(cancellationToken);

        var subscriptionRows = await db.OrganizationSubscriptions
            .Where(subscription => subscription.Status == SubscriptionStatus.Active
                                   || subscription.Status == SubscriptionStatus.Trialing)
            .Select(subscription => new { subscription.PlanTier, subscription.PriceLkr })
            .ToListAsync(cancellationToken);

        var organizationsWithBillingRow = await db.OrganizationSubscriptions
            .Select(subscription => subscription.OrganizationId)
            .Distinct()
            .CountAsync(cancellationToken);

        // Membership, not the legacy denormalised User.OrganizationId column, which the
        // Organization model documents as migrated to memberships.
        var membershipRows = await db.OrganizationMemberships
            .Where(membership => membership.Status == MembershipStatus.Active)
            .Join(
                db.Organizations,
                membership => membership.OrganizationId,
                organization => organization.Id,
                (membership, organization) => new { organization.PlanTier, membership.UserId })
            .ToListAsync(cancellationToken);

        var organizationCounts = organizationRows
            .GroupBy(row => Tier(row.PlanTier))
            .ToDictionary(
                group => group.Key,
                group => (Total: group.Count(), Active: group.Count(row => row.IsActive)));
        var billedCounts = subscriptionRows
            .GroupBy(row => Tier(row.PlanTier))
            .ToDictionary(
                group => group.Key,
                group => (Count: group.Count(), Price: group.Sum(row => row.PriceLkr)));
        var userCounts = membershipRows
            .GroupBy(row => Tier(row.PlanTier))
            .ToDictionary(group => group.Key, group => group.Select(row => row.UserId).Distinct().Count());

        var totalOrganizations = organizationRows.Count;
        var tiers = OrderedTiers
            .Select(tier =>
            {
                var counts = organizationCounts.TryGetValue(tier, out var found) ? found : (Total: 0, Active: 0);
                var billed = billedCounts.TryGetValue(tier, out var billedFound)
                    ? billedFound
                    : (Count: 0, Price: 0m);
                return new PlanMixItemDto(
                    tier,
                    IsFree(tier),
                    counts.Total,
                    counts.Active,
                    billed.Count,
                    userCounts.TryGetValue(tier, out var users) ? users : 0,
                    billed.Price);
            })
            .ToList();

        var free = Side(tiers.Where(item => item.IsFree).ToList(), totalOrganizations);
        var premium = Side(tiers.Where(item => !item.IsFree).ToList(), totalOrganizations);

        return new BusinessPlanMixDto(
            timeProvider.GetUtcNow().UtcDateTime,
            tiers,
            free,
            premium,
            totalOrganizations,
            organizationsWithBillingRow,
            tiers.Sum(item => item.MonthlyPriceLkr),
            DataQuality());
    }

    private static PlanMixSideDto Side(IReadOnlyList<PlanMixItemDto> items, int totalOrganizations)
    {
        var count = items.Sum(item => item.OrganizationCount);
        return new PlanMixSideDto(
            count,
            items.Sum(item => item.UserCount),
            items.Sum(item => item.MonthlyPriceLkr),
            totalOrganizations == 0 ? 0d : (double)count / totalOrganizations);
    }

    private static bool IsFree(string tier) =>
        string.Equals(tier, nameof(PlanTier.Seed), StringComparison.Ordinal);

    private static string Tier(PlanTier tier) => tier.ToString();

    // ── S-47…S-49 (phase 3) ───────────────────────────────────────────────────────────────

    public Task<BusinessSubscriptionTrendDto> GetSubscriptionTrendAsync(
        BusinessWindow window,
        string? cacheKey,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("S-47 is delivered in business-KPIs phase 3.");

    public Task<BusinessUsageDto> GetUsageAsync(
        BusinessWindow window,
        Guid? organizationId,
        string? cacheKey,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("S-48 is delivered in business-KPIs phase 3.");

    public Task<BusinessOrganizationUsageDto> GetOrganizationUsageAsync(
        BusinessWindow window,
        BusinessRankingMetric metric,
        int limit,
        string? cacheKey,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("S-49 is delivered in business-KPIs phase 3.");

    // ── Shared helpers ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A bucket is partial when it does not occupy its full natural interval inside the window:
    /// the leading bucket clipped by <c>from</c>, or the trailing bucket the window's <c>to</c>
    /// falls inside.
    /// </summary>
    private static bool IsPartial(DateTime bucketStart, BusinessWindow window) =>
        bucketStart < window.From
        || BusinessKpiValidation.Advance(bucketStart, window.Granularity) > window.To;

    private static Dictionary<DateTime, int> CountByBucket(
        IEnumerable<DateTime> instants,
        string granularity)
    {
        var counts = new Dictionary<DateTime, int>();
        foreach (var instant in instants)
        {
            var bucket = BusinessKpiValidation.Truncate(instant, granularity);
            counts[bucket] = counts.TryGetValue(bucket, out var current) ? current + 1 : 1;
        }

        return counts;
    }

    /// <summary>
    /// Sums a day-keyed dictionary over one bucket. The axis is day-keyed because every source
    /// timestamp carries a time component, so the fold walks the bucket's days.
    /// </summary>
    private static int SumForBucket(
        IReadOnlyDictionary<DateTime, int> counts,
        DateTime bucketStart,
        string granularity)
    {
        var bucketEnd = BusinessKpiValidation.Advance(bucketStart, granularity);
        var total = 0;
        for (var day = bucketStart.Date; day < bucketEnd; day = day.AddDays(1))
        {
            if (counts.TryGetValue(day, out var value))
            {
                total += value;
            }
        }

        return total;
    }

    private BusinessDataQualityDto DataQuality(
        bool userAttributionAvailable = true,
        string? notes = null) =>
        new(
            UserAttributionAvailable: userAttributionAvailable,
            UnresolvedAttributionCount: identities.UnresolvedCount,
            SubscriptionHistoryBackfilled: false,
            LastActivityIsReconstructed: false,
            AgentMetricsUninstrumented: false,
            Notes: notes is null ? [] : [notes]);

    private static BusinessWindowDto ToDto(BusinessWindow window) =>
        new(window.From, window.To, window.Granularity, window.TimeZone, window.BucketCount);
}
