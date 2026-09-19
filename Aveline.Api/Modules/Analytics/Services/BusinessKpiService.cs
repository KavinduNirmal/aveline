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
                :
                [
                    "User attribution is unavailable for this window: no API request carried a resolved "
                    + "user id, so the active-user measures are reported as not measured rather than as "
                    + "zero. SignalR-only sessions are not counted by this definition.",
                ]);

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

    // ── S-47 subscription trend ───────────────────────────────────────────────────────────

    public async Task<BusinessSubscriptionTrendDto> GetSubscriptionTrendAsync(
        BusinessWindow window,
        string? cacheKey,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.OrganizationSubscriptionSnapshots
            .Where(snapshot => snapshot.SnapshotDay >= window.From && snapshot.SnapshotDay < window.To)
            .Select(snapshot => new
            {
                snapshot.SnapshotDay,
                snapshot.OrganizationId,
                snapshot.PlanTier,
                snapshot.Status,
                snapshot.IsBackfilled,
                snapshot.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var activeStatuses = new[] { SubscriptionStatus.Active, SubscriptionStatus.Trialing };
        var active = rows
            .Where(row => activeStatuses.Contains(row.Status))
            .ToList();

        var axis = BusinessKpiValidation.BuildAxis(window.From, window.To, window.Granularity);
        var series = new List<SubscriptionTrendPointDto>(axis.Count);

        foreach (var bucketStart in axis)
        {
            var bucketEnd = BusinessKpiValidation.Advance(bucketStart, window.Granularity);

            // The latest snapshot day inside the bucket: a snapshot is a point-in-time reading,
            // so a bucket is summarised by its closing snapshot rather than by a sum.
            var inBucket = active
                .Where(row => row.SnapshotDay >= bucketStart && row.SnapshotDay < bucketEnd)
                .ToList();
            var closingDay = inBucket.Count == 0 ? (DateTime?)null : inBucket.Max(row => row.SnapshotDay);
            var closing = closingDay is null
                ? []
                : inBucket.Where(row => row.SnapshotDay == closingDay).ToList();

            var byTier = closing
                .GroupBy(row => Tier(row.PlanTier))
                .ToDictionary(group => group.Key, group => group.Count());

            var started = rows.Count(row => row.CreatedAt >= bucketStart
                                            && row.CreatedAt < bucketEnd
                                            && row.SnapshotDay == window.To.Date);
            var cancelled = inBucket.Count(row => row.Status == SubscriptionStatus.Cancelled);

            series.Add(new SubscriptionTrendPointDto(
                bucketStart,
                IsPartial(bucketStart, window),
                closing.Select(row => row.OrganizationId).Distinct().Count(),
                byTier,
                started,
                cancelled,
                closing.Count > 0 && closing.All(row => row.IsBackfilled)));
        }

        var opening = await ClosingActiveAsync(window.From, cancellationToken);
        var closingActive = series.Count == 0 ? opening : series[^1].ActiveTotal;
        var cancelledTotal = series.Sum(point => point.Cancelled);
        var churnRate = opening > 0 ? (decimal)cancelledTotal / opening : 0m;

        var anyBackfilled = series.Any(point => point.IsBackfilled);

        return new BusinessSubscriptionTrendDto(
            ToDto(window),
            series,
            opening,
            closingActive,
            churnRate,
            DataQuality(
                notes: anyBackfilled
                    ?
                    [
                        "Buckets marked approximate were reconstructed from the audit ledger rather "
                        + "than snapshotted, and their tier comes from Organizations.PlanTier.",
                    ]
                    : null,
                subscriptionHistoryBackfilled: anyBackfilled));
    }

    private async Task<int> ClosingActiveAsync(DateTime asOf, CancellationToken cancellationToken)
    {
        var activeStatuses = new[] { SubscriptionStatus.Active, SubscriptionStatus.Trialing };
        var latestDay = await db.OrganizationSubscriptionSnapshots
            .Where(snapshot => snapshot.SnapshotDay < asOf)
            .Select(snapshot => (DateTime?)snapshot.SnapshotDay)
            .MaxAsync(cancellationToken);

        if (latestDay is null)
        {
            return 0;
        }

        return await db.OrganizationSubscriptionSnapshots
            .Where(snapshot => snapshot.SnapshotDay == latestDay
                               && activeStatuses.Contains(snapshot.Status))
            .Select(snapshot => snapshot.OrganizationId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    // ── S-48 usage ────────────────────────────────────────────────────────────────────────

    public async Task<BusinessUsageDto> GetUsageAsync(
        BusinessWindow window,
        Guid? organizationId,
        string? cacheKey,
        CancellationToken cancellationToken = default)
    {
        var conversations = await db.Conversations
            .Where(conversation => organizationId == null || conversation.OrganizationId == organizationId)
            .Select(conversation => new { conversation.Id, conversation.OrganizationId })
            .ToListAsync(cancellationToken);
        var conversationIds = conversations.Select(conversation => conversation.Id).ToList();

        var messageRows = conversationIds.Count == 0
            ? []
            : await db.Messages
                .Where(message => conversationIds.Contains(message.ConversationId)
                                  && message.CreatedAt >= window.From
                                  && message.CreatedAt < window.To)
                .Select(message => new { message.ConversationId, message.CreatedAt })
                .ToListAsync(cancellationToken);

        var agentRows = await db.DailyAgentMetrics
            .Where(metric => metric.Day >= window.From
                             && metric.Day < window.To
                             && (organizationId == null || metric.OrganizationId == organizationId))
            .Select(metric => new { metric.Day, metric.RunCount })
            .ToListAsync(cancellationToken);

        // A platform total deliberately includes unattributed requests (BR-6.1): the request
        // happened. A scoped read excludes them, because they belong to no organization.
        var apiRows = await db.ApiRequestMetrics
            .Where(metric => metric.WindowSize == "day"
                             && metric.WindowStart >= window.From
                             && metric.WindowStart < window.To
                             && (organizationId == null || metric.OrganizationId == organizationId))
            .Select(metric => new { metric.WindowStart, metric.RequestCount })
            .ToListAsync(cancellationToken);

        var billingRows = await db.DailyBillingMetrics
            .Where(metric => metric.Day >= window.From
                             && metric.Day < window.To
                             && (organizationId == null || metric.OrganizationId == organizationId))
            .Select(metric => new { metric.Day, metric.BlossomUnits, metric.ActualCostUsd })
            .ToListAsync(cancellationToken);

        var messages = CountByBucket(
            messageRows.Select(row => row.CreatedAt), window.Granularity);
        var agents = SumByBucket(
            agentRows.Select(row => (row.Day, (decimal)row.RunCount)), window.Granularity);
        var api = SumByBucket(
            apiRows.Select(row => (row.WindowStart, (decimal)row.RequestCount)), window.Granularity);
        var blossoms = SumByBucket(
            billingRows.Select(row => (row.Day, row.BlossomUnits)), window.Granularity);
        var cost = SumByBucket(
            billingRows.Select(row => (row.Day, row.ActualCostUsd)), window.Granularity);

        var axis = BusinessKpiValidation.BuildAxis(window.From, window.To, window.Granularity);
        var series = axis
            .Select(bucketStart => new UsageTrendPointDto(
                bucketStart,
                IsPartial(bucketStart, window),
                SumForBucket(messages, bucketStart, window.Granularity),
                (int)SumDecimalForBucket(agents, bucketStart, window.Granularity),
                (long)SumDecimalForBucket(api, bucketStart, window.Granularity),
                SumDecimalForBucket(blossoms, bucketStart, window.Granularity),
                SumDecimalForBucket(cost, bucketStart, window.Granularity)))
            .ToList();

        var totals = new UsageTotalsDto(
            series.Sum(point => point.MessagesSent),
            series.Sum(point => point.AgentRuns),
            series.Sum(point => point.ApiRequests),
            series.Sum(point => point.BlossomUnits),
            series.Sum(point => point.ActualCostUsd));

        var notes = new List<string>
        {
            "Agent-run counts come from the DailyAgentMetrics rollup, which has no user dimension: "
            + "per-user agent-run attribution is not available, so these are organization-level totals.",
        };
        if (organizationId is null)
        {
            notes.Add(
                "This platform total includes API requests that carry no organization or user "
                + "attribution (BR-6.1); those requests happened and are counted.");
        }

        var quality = DataQuality(
            agentMetricsUninstrumented: true,
            notes: notes);

        return new BusinessUsageDto(ToDto(window), organizationId, series, totals, quality);
    }

    // ── S-49 organization ranking ─────────────────────────────────────────────────────────

    public async Task<BusinessOrganizationUsageDto> GetOrganizationUsageAsync(
        BusinessWindow window,
        BusinessRankingMetric metric,
        int limit,
        string? cacheKey,
        CancellationToken cancellationToken = default)
    {
        var conversations = await db.Conversations
            .Select(conversation => new { conversation.Id, conversation.OrganizationId })
            .ToListAsync(cancellationToken);
        var conversationIds = conversations.Select(conversation => conversation.Id).ToList();
        var organizationByConversation = conversations.ToDictionary(
            conversation => conversation.Id, conversation => conversation.OrganizationId);

        var messageRows = conversationIds.Count == 0
            ? []
            : await db.Messages
                .Where(message => conversationIds.Contains(message.ConversationId)
                                  && message.CreatedAt >= window.From
                                  && message.CreatedAt < window.To)
                .Select(message => new { message.ConversationId, message.CreatedAt })
                .ToListAsync(cancellationToken);

        var agentRows = await db.DailyAgentMetrics
            .Where(row => row.Day >= window.From && row.Day < window.To && row.OrganizationId != null)
            .Select(row => new { OrganizationId = row.OrganizationId!.Value, row.Day, row.RunCount })
            .ToListAsync(cancellationToken);

        var apiRows = await db.ApiRequestMetrics
            .Where(row => row.WindowSize == "day"
                          && row.WindowStart >= window.From
                          && row.WindowStart < window.To
                          && row.OrganizationId != null)
            .Select(row => new
            {
                OrganizationId = row.OrganizationId!.Value,
                row.WindowStart,
                row.RequestCount,
            })
            .ToListAsync(cancellationToken);

        var billingRows = await db.DailyBillingMetrics
            .Where(row => row.Day >= window.From && row.Day < window.To)
            .Select(row => new { row.OrganizationId, row.Day, row.BlossomUnits })
            .ToListAsync(cancellationToken);

        var auditRows = await db.AuditLogEntries
            .Where(row => row.OrganizationId != null
                          && row.CreatedAt >= window.From
                          && row.CreatedAt < window.To)
            .Select(row => new { OrganizationId = row.OrganizationId!.Value, row.CreatedAt })
            .ToListAsync(cancellationToken);

        var conversationActivity = await db.Conversations
            .Where(conversation => conversation.LastMessageAt != null
                                   && conversation.LastMessageAt >= window.From
                                   && conversation.LastMessageAt < window.To)
            .Select(conversation => new { conversation.OrganizationId, LastMessageAt = conversation.LastMessageAt!.Value })
            .ToListAsync(cancellationToken);

        var messagesByOrganization = messageRows
            .GroupBy(row => organizationByConversation[row.ConversationId])
            .ToDictionary(group => group.Key, group => (long)group.Count());
        var agentsByOrganization = agentRows
            .GroupBy(row => row.OrganizationId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.RunCount));
        var apiByOrganization = apiRows
            .GroupBy(row => row.OrganizationId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.RequestCount));
        var blossomsByOrganization = billingRows
            .GroupBy(row => row.OrganizationId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.BlossomUnits));

        DateTime? LastActivity(Guid organizationId)
        {
            DateTime? latest = null;
            foreach (var candidate in new[]
                     {
                         conversationActivity.FirstOrDefault(row => row.OrganizationId == organizationId)?.LastMessageAt,
                         agentRows.Where(row => row.OrganizationId == organizationId)
                             .Select(row => (DateTime?)row.Day).Max(),
                         apiRows.Where(row => row.OrganizationId == organizationId)
                             .Select(row => (DateTime?)row.WindowStart).Max(),
                         auditRows.Where(row => row.OrganizationId == organizationId)
                             .Select(row => (DateTime?)row.CreatedAt).Max(),
                     })
            {
                if (candidate is { } value && (latest is null || value > latest))
                {
                    latest = value;
                }
            }

            return latest;
        }

        bool IsMeasured(Guid organizationId) =>
            messagesByOrganization.ContainsKey(organizationId)
            || agentsByOrganization.ContainsKey(organizationId)
            || apiByOrganization.ContainsKey(organizationId)
            || blossomsByOrganization.ContainsKey(organizationId);

        var candidates = await db.Organizations
            .Select(organization => new { organization.Id, organization.Name, organization.PlanTier })
            .ToListAsync(cancellationToken);

        var ranked = candidates
            .Select(organization => new
            {
                organization.Id,
                organization.Name,
                organization.PlanTier,
                MessagesSent = messagesByOrganization.GetValueOrDefault(organization.Id),
                AgentRuns = agentsByOrganization.GetValueOrDefault(organization.Id),
                ApiRequests = apiByOrganization.GetValueOrDefault(organization.Id),
                BlossomUnits = blossomsByOrganization.GetValueOrDefault(organization.Id),
                LastActivityAt = LastActivity(organization.Id),
            })
            .Where(row => IsMeasured(row.Id))
            .ToList();

        var ordered = ranked
            .OrderByDescending(row => metric switch
            {
                BusinessRankingMetric.Messages => (double)row.MessagesSent,
                BusinessRankingMetric.AgentRuns => row.AgentRuns,
                BusinessRankingMetric.BlossomUnits => (double)row.BlossomUnits,
                _ => row.ApiRequests,
            })
            // A deterministic tiebreak keeps paging stable.
            .ThenBy(row => row.Id)
            .ToList();

        var items = ordered
            .Take(limit)
            .Select((row, index) => new OrganizationUsageItemDto(
                index + 1,
                row.Id,
                row.Name,
                Tier(row.PlanTier),
                row.MessagesSent,
                row.AgentRuns,
                row.ApiRequests,
                row.BlossomUnits,
                row.LastActivityAt,
                row.LastActivityAt is null
                    ? null
                    : (int)(timeProvider.GetUtcNow().UtcDateTime.Date - row.LastActivityAt.Value.Date).TotalDays))
            .ToList();

        return new BusinessOrganizationUsageDto(
            MetricName(metric),
            window.From,
            window.To,
            items,
            ordered.Count,
            DataQuality(
                lastActivityIsReconstructed: true,
                notes:
                [
                    "Last activity is a greatest-of reconstruction over conversation, agent-run, "
                    + "API-metric and audit timestamps, not a recorded fact: no per-day user-activity "
                    + "table exists.",
                ]));
    }

    private static string MetricName(BusinessRankingMetric metric) => metric switch
    {
        BusinessRankingMetric.Messages => "messages",
        BusinessRankingMetric.AgentRuns => "agentRuns",
        BusinessRankingMetric.BlossomUnits => "blossomUnits",
        _ => "apiRequests",
    };

    // ── Shared helpers ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A bucket is partial when it does not occupy its full natural interval inside the window:
    /// the leading bucket clipped by <c>from</c>, or the trailing bucket the window's <c>to</c>
    /// falls inside.
    /// </summary>
    private static bool IsPartial(DateTime bucketStart, BusinessWindow window) =>
        bucketStart < window.From
        || BusinessKpiValidation.Advance(bucketStart, window.Granularity) > window.To;

    /// <summary>
    /// Sums a day-keyed decimal dictionary over one bucket. The axis is day-keyed because every
    /// source timestamp carries a time component, so the fold walks the bucket's days.
    /// </summary>
    private static decimal SumDecimalForBucket(
        IReadOnlyDictionary<DateTime, decimal> values,
        DateTime bucketStart,
        string granularity)
    {
        var bucketEnd = BusinessKpiValidation.Advance(bucketStart, granularity);
        var total = 0m;
        for (var day = bucketStart.Date; day < bucketEnd; day = day.AddDays(1))
        {
            if (values.TryGetValue(day, out var value))
            {
                total += value;
            }
        }

        return total;
    }

    private static Dictionary<DateTime, decimal> SumByBucket(
        IEnumerable<(DateTime Instant, decimal Value)> rows,
        string granularity)
    {
        var totals = new Dictionary<DateTime, decimal>();
        foreach (var (instant, value) in rows)
        {
            var bucket = BusinessKpiValidation.Truncate(instant, granularity);
            totals[bucket] = totals.TryGetValue(bucket, out var current) ? current + value : value;
        }

        return totals;
    }

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
        bool subscriptionHistoryBackfilled = false,
        bool lastActivityIsReconstructed = false,
        bool agentMetricsUninstrumented = false,
        IReadOnlyList<string>? notes = null) =>
        new(
            UserAttributionAvailable: userAttributionAvailable,
            UnresolvedAttributionCount: identities.UnresolvedCount,
            SubscriptionHistoryBackfilled: subscriptionHistoryBackfilled,
            LastActivityIsReconstructed: lastActivityIsReconstructed,
            AgentMetricsUninstrumented: agentMetricsUninstrumented,
            Notes: notes ?? []);

    private static BusinessWindowDto ToDto(BusinessWindow window) =>
        new(window.From, window.To, window.Granularity, window.TimeZone, window.BucketCount);
}
