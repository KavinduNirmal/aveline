using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Analytics.Models;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Statistics.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Analytics.Jobs;

/// <summary>
/// Daily 02:00 UTC snapshot of every active organization's tier and billing state (S-47).
/// Deliberately after <c>BillingRollupJob</c>'s 01:30 so a tier change made the same day is
/// already settled when the row is written.
/// </summary>
/// <remarks>
/// <b>The tier axis is <c>Organizations</c>, not <c>OrganizationSubscriptions</c>.</b> An
/// organization that never changed or cancelled its plan has no billing row (D-8), so a
/// snapshot that iterated the subscription table would silently omit most organizations. Every
/// active organization gets a row, with <see cref="OrganizationSubscriptionSnapshot.HasBillingRow"/>
/// recording whether a billing row existed.
/// </remarks>
public sealed class OrganizationSubscriptionSnapshotJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<OrganizationSubscriptionSnapshotJob> logger)
    : StatisticsJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "org-subscription-snapshot";

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override TimeSpan? InitialDelay => NextDailyRunAt(DateTime.UtcNow, hourUtc: 2, minuteUtc: 0);

    /// <summary>Snapshots yesterday (the last closed day) and prunes beyond the retention window.</summary>
    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var yesterday = DateTime.UtcNow.Date.AddDays(-1);
        var written = await RecomputeDayAsync(db, yesterday, cancellationToken);
        await PruneAsync(db, yesterday, retentionDays: 400, cancellationToken);
        return written;
    }

    /// <summary>
    /// Recomputes and replaces one day's rows. Delete-and-replace makes a re-run idempotent:
    /// the same input produces the same rows, never a duplicate.
    /// </summary>
    public static async Task<int> RecomputeDayAsync(
        AppDbContext db,
        DateTime day,
        CancellationToken cancellationToken = default)
    {
        var start = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Utc);
        var dayEnd = start.AddDays(1);

        var organizations = await db.Organizations
            .Where(organization => organization.CreatedAt < dayEnd
                                   && (organization.SuspendedAt == null || organization.SuspendedAt > dayEnd))
            .Select(organization => new { organization.Id, organization.PlanTier })
            .ToListAsync(cancellationToken);

        // Delete-and-replace inside one transaction, so a reader sees either the old day or the
        // new day and never a half-written one.
        var existing = await db.OrganizationSubscriptionSnapshots
            .Where(snapshot => snapshot.SnapshotDay == start)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            db.OrganizationSubscriptionSnapshots.RemoveRange(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (organizations.Count == 0)
        {
            return 0;
        }

        var organizationIds = organizations.Select(organization => organization.Id).ToList();
        var subscriptions = await db.OrganizationSubscriptions
            .Where(subscription => organizationIds.Contains(subscription.OrganizationId))
            .ToDictionaryAsync(subscription => subscription.OrganizationId, cancellationToken);

        var rows = organizations
            .Select(organization =>
            {
                subscriptions.TryGetValue(organization.Id, out var subscription);
                return new OrganizationSubscriptionSnapshot
                {
                    OrganizationId = organization.Id,
                    SnapshotDay = start,
                    // The organization is authoritative for the tier; the billing row supplements.
                    PlanTier = organization.PlanTier,
                    Status = subscription?.Status ?? SubscriptionStatus.Active,
                    HasBillingRow = subscription is not null,
                    SeatsIncluded = subscription?.SeatsIncluded ?? 0,
                    PriceLkr = subscription?.PriceLkr ?? 0m,
                    BillingCycle = subscription?.BillingCycle ?? BillingCycle.Monthly,
                    IsBackfilled = false,
                };
            })
            .ToList();

        db.OrganizationSubscriptionSnapshots.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);

        return rows.Count;
    }

    /// <summary>
    /// Deletes snapshots older than the retention window, keeping the boundary day. Follows
    /// <c>ApiStatsRetentionJob</c>'s shape.
    /// </summary>
    public static async Task<int> PruneAsync(
        AppDbContext db,
        DateTime asOf,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        var cutoff = new DateTime(asOf.Year, asOf.Month, asOf.Day, 0, 0, 0, DateTimeKind.Utc)
            .AddDays(-Math.Max(0, retentionDays));

        var stale = await db.OrganizationSubscriptionSnapshots
            .Where(snapshot => snapshot.SnapshotDay < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        db.OrganizationSubscriptionSnapshots.RemoveRange(stale);
        await db.SaveChangesAsync(cancellationToken);

        return stale.Count;
    }

    /// <summary>Delay from <paramref name="now"/> to the next daily run at the given UTC time.</summary>
    internal static TimeSpan NextDailyRunAt(DateTime now, int hourUtc, int minuteUtc)
    {
        var next = new DateTime(now.Year, now.Month, now.Day, hourUtc, minuteUtc, 0, DateTimeKind.Utc);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        return next - now;
    }
}
