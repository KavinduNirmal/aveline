using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Analytics.Models;
using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Analytics.Jobs;

/// <summary>
/// One-shot reconstruction of subscription history from the audit ledger, for the days before
/// the snapshot table's first real row.
/// </summary>
/// <remarks>
/// <b>This exists to avoid reproducing defect D-1.</b> The shipped
/// <c>BillingStatisticsService.GetPlanChangesAsync</c> falls back to <c>toTier = "Grow"</c> —
/// a tier that does not exist in <see cref="PlanTier"/> — when it cannot parse the audit JSON.
/// <see cref="ReconstructTier"/> returns <c>null</c> for an unparseable payload, for a missing
/// property, for a non-string value, and for any string that is not one of the five tiers.
/// </remarks>
public static class SubscriptionBackfill
{
    private const string PlanChangedAction = "org.plan.changed";
    private const string TierProperty = "PlanTier";

    /// <summary>
    /// Reconstructs a tier from an audit JSON snapshot, or <c>null</c> when it cannot be
    /// determined truthfully.
    /// </summary>
    public static PlanTier? ReconstructTier(string? json, string propertyName = TierProperty)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(propertyName, out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = value.GetString();
            return !string.IsNullOrWhiteSpace(text)
                   && Enum.TryParse<PlanTier>(text, ignoreCase: true, out var tier)
                   && Enum.IsDefined(tier)
                ? tier
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reconstructs the tier history for every organization that has a plan-change audit entry
    /// in the window, writing one backfilled row per governed day.
    /// </summary>
    /// <returns>The number of backfilled rows written.</returns>
    public static async Task<int> RunAsync(
        AppDbContext db,
        DateTime from,
        DateTime to,
        int maxWindowDays,
        CancellationToken cancellationToken = default)
    {
        var start = new DateTime(from.Year, from.Month, from.Day, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(to.Year, to.Month, to.Day, 0, 0, 0, DateTimeKind.Utc);

        // The cap is a hard ceiling on the number of days a backfill may materialise, so a
        // misconfigured window cannot create an unbounded series. Inclusive of both ends.
        var earliest = end.AddDays(-Math.Max(1, maxWindowDays) + 1);
        if (start < earliest)
        {
            start = earliest;
        }

        if (start > end)
        {
            return 0;
        }

        var auditRows = await db.AuditLogEntries
            .Where(entry => entry.Action == PlanChangedAction
                            && entry.OrganizationId != null
                            && entry.CreatedAt >= start
                            && entry.CreatedAt < end.AddDays(1))
            .Select(entry => new { entry.OrganizationId, entry.AfterJson, entry.CreatedAt })
            .ToListAsync(cancellationToken);

        if (auditRows.Count == 0)
        {
            return 0;
        }

        var organizations = await db.Organizations
            .Where(organization => organization.CreatedAt < end.AddDays(1))
            .Select(organization => new { organization.Id, organization.PlanTier })
            .ToListAsync(cancellationToken);

        // Days already covered by a real snapshot are never overwritten.
        var realDays = await db.OrganizationSubscriptionSnapshots
            .Where(snapshot => snapshot.SnapshotDay >= start
                               && snapshot.SnapshotDay <= end
                               && !snapshot.IsBackfilled)
            .Select(snapshot => new { snapshot.OrganizationId, snapshot.SnapshotDay })
            .ToListAsync(cancellationToken);
        var protectedDays = realDays
            .Select(entry => (entry.OrganizationId, entry.SnapshotDay))
            .ToHashSet();

        var changesByOrganization = auditRows
            .GroupBy(entry => entry.OrganizationId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(entry => entry.CreatedAt)
                    .Select(entry => (entry.CreatedAt, Tier: ReconstructTier(entry.AfterJson)))
                    .ToList());

        var existingBackfilled = await db.OrganizationSubscriptionSnapshots
            .Where(snapshot => snapshot.SnapshotDay >= start
                               && snapshot.SnapshotDay <= end
                               && snapshot.IsBackfilled)
            .ToListAsync(cancellationToken);
        if (existingBackfilled.Count > 0)
        {
            db.OrganizationSubscriptionSnapshots.RemoveRange(existingBackfilled);
            await db.SaveChangesAsync(cancellationToken);
        }

        var rows = new List<OrganizationSubscriptionSnapshot>();
        foreach (var organization in organizations)
        {
            if (!changesByOrganization.TryGetValue(organization.Id, out var changes))
            {
                continue;
            }

            for (var day = start; day <= end; day = day.AddDays(1))
            {
                if (protectedDays.Contains((organization.Id, day)))
                {
                    continue;
                }

                // The tier in force at the end of `day`: the latest change on or before it. Before
                // the earliest change the organization's live tier is the only evidence available
                // and is reported as this series' floor, which the note on the response says.
                // Nulls (unparseable payloads) are skipped rather than falling through to a
                // phantom literal, so a broken audit row leaves the previous known tier.
                var tier = organization.PlanTier;
                foreach (var change in changes)
                {
                    // The change governs the day it happened on and every day after it. A change
                    // later in the same day has still happened by the end of that day.
                    if (change.CreatedAt.Date <= day && change.Tier is { } parsed)
                    {
                        tier = parsed;
                    }
                }

                rows.Add(new OrganizationSubscriptionSnapshot
                {
                    OrganizationId = organization.Id,
                    SnapshotDay = day,
                    PlanTier = tier,
                    Status = SubscriptionStatus.Active,
                    HasBillingRow = false,
                    SeatsIncluded = 0,
                    PriceLkr = 0m,
                    BillingCycle = BillingCycle.Monthly,
                    IsBackfilled = true,
                });
            }
        }

        if (rows.Count == 0)
        {
            return 0;
        }

        db.OrganizationSubscriptionSnapshots.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);

        return rows.Count;
    }
}
