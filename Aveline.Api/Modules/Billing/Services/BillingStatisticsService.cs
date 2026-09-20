using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.DTOs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Services;

public class BillingStatisticsService(
    AppDbContext db,
    IBlossomService blossomService,
    IEntitlementResolver entitlementResolver) : IBillingStatisticsService
{
    public async Task<BurnRateResponseDto> GetBurnRateAsync(
        Guid organizationId, string window, CancellationToken cancellationToken = default)
    {
        var days = window switch
        {
            "7d" => 7,
            "14d" => 14,
            _ => 30,
        };

        var now = DateTime.UtcNow;
        var start = now.AddDays(-days);

        var balance = await blossomService.GetBalanceAsync(organizationId, cancellationToken);

        var usage = await db.AiUsageRecords
            .Where(r => r.OrganizationId == organizationId && r.CreatedAt >= start)
            .SumAsync(r => (decimal?)r.BlossomUnits, cancellationToken) ?? 0m;

        var burnRatePerDay = days > 0 ? Math.Round(usage / days, 4) : 0m;
        DateTime? projectedExhaustion = null;

        if (burnRatePerDay > 0 && balance.BlossomRemaining > 0)
        {
            var daysRemaining = (double)(balance.BlossomRemaining / burnRatePerDay);
            if (daysRemaining <= 3650)
            {
                projectedExhaustion = now.AddDays(daysRemaining);
            }
        }

        // M-6: derive from the agent rows in the window; never assert a flag because an
        // instrument exists. An empty window must report every flag as false.
        var dataQuality = await DeriveAgentDataQualityAsync(
            organizationId, start, now, cancellationToken);

        return new BurnRateResponseDto(
            Window: $"{days}d",
            BurnRatePerDay: burnRatePerDay,
            ProjectedExhaustionAt: projectedExhaustion,
            CurrentBalance: balance.BlossomRemaining,
            AverageDailyUsage: burnRatePerDay,
            DataQuality: dataQuality);
    }

    public async Task<ActiveCustomersDto> GetActiveCustomersAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-90);

        var interactionCustomerIds = db.CustomerInteractions
            .Where(i => i.OrganizationId == organizationId && i.CreatedAt >= cutoff)
            .Select(i => i.CustomerId);

        var orderCustomerIds = db.Orders
            .Where(o => o.OrganizationId == organizationId && o.CreatedAt >= cutoff)
            .Select(o => o.CustomerId);

        var directCustomers = db.Customers
            .Where(c => c.OrganizationId == organizationId && (c.UpdatedAt >= cutoff || c.CreatedAt >= cutoff))
            .Select(c => c.Id);

        var activeCount = await interactionCustomerIds
            .Union(orderCustomerIds)
            .Union(directCustomers)
            .Distinct()
            .CountAsync(cancellationToken);

        var totalCount = await db.Customers
            .CountAsync(c => c.OrganizationId == organizationId, cancellationToken);

        return new ActiveCustomersDto(
            OrganizationId: organizationId,
            ActiveCount: activeCount,
            TotalCount: totalCount,
            AsOf: now);
    }

    public async Task<StaffSeatsDto> GetStaffSeatsAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var memberships = await db.OrganizationMemberships
            .Where(m => m.OrganizationId == organizationId && m.Status == MembershipStatus.Active)
            .ToListAsync(cancellationToken);

        var allowed = await entitlementResolver.GetDecimalAsync(
            organizationId, "staff.max", 1m, at: null, cancellationToken);

        var roleMap = memberships
            .GroupBy(m => m.BoutiqueRole)
            .ToDictionary(g => g.Key, g => g.Count());

        return new StaffSeatsDto(
            OrganizationId: organizationId,
            ActiveSeats: memberships.Count,
            AllowedSeats: (int)allowed,
            RoleBreakdown: roleMap,
            AsOf: now);
    }

    public async Task<BillingProfitabilityDto> GetProfitabilityAsync(
        DateTime? from, DateTime? to, string? groupBy, CancellationToken cancellationToken = default)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end = to ?? DateTime.UtcNow;
        var mode = (groupBy ?? "model").ToLowerInvariant();

        var query = db.AiUsageRecords
            .Include(r => r.Organization)
            .Where(r => r.CreatedAt >= start && r.CreatedAt <= end);

        var records = await query.ToListAsync(cancellationToken);

        var series = records
            .GroupBy(r => mode switch
            {
                "tier" => (r.Organization?.PlanTier ?? PlanTier.Seed).ToString(),
                "provider" => r.Provider,
                _ => r.Model,
            })
            .Select(group => new ProfitabilityItemDto(
                Dimension: group.Key,
                RequestCount: group.Count(),
                BlossomRevenue: group.Sum(r => r.BlossomUnits),
                ActualCostUsd: group.Sum(r => r.ActualCostUsd),
                MarginLkr: null))
            .OrderByDescending(item => item.BlossomRevenue)
            .ToList();

        var totalRevenue = series.Sum(s => s.BlossomRevenue);
        var totalCost = series.Sum(s => s.ActualCostUsd);

        // M-6: the profitability window is system-wide, so derive from every run in it.
        var dataQuality = await DeriveAgentDataQualityAsync(null, start, end, cancellationToken);

        return new BillingProfitabilityDto(
            GroupBy: mode,
            Series: series,
            TotalBlossomRevenue: totalRevenue,
            TotalCostUsd: totalCost,
            TotalMarginLkr: null,
            DataQuality: dataQuality);
    }

    public async Task<OrgUsageRankingPageDto> GetOrgUsageRankingAsync(
        DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end = to ?? DateTime.UtcNow;
        var p = Math.Max(1, page);
        var ps = Math.Clamp(pageSize, 1, 100);

        var grouped = await db.AiUsageRecords
            .Include(r => r.Organization)
            .Where(r => r.CreatedAt >= start && r.CreatedAt <= end)
            .GroupBy(r => new { r.OrganizationId, Name = r.Organization != null ? r.Organization.Name : "Unknown", Tier = r.Organization != null ? r.Organization.PlanTier : PlanTier.Seed })
            .Select(g => new
            {
                g.Key.OrganizationId,
                g.Key.Name,
                g.Key.Tier,
                BlossomUnits = g.Sum(r => r.BlossomUnits),
                RequestCount = g.Count(),
            })
            .OrderByDescending(g => g.BlossomUnits)
            .ToListAsync(cancellationToken);

        var total = grouped.Count;
        var items = grouped
            .Skip((p - 1) * ps)
            .Take(ps)
            .Select((g, idx) => new OrgUsageRankItemDto(
                Rank: ((p - 1) * ps) + idx + 1,
                OrganizationId: g.OrganizationId,
                OrganizationName: g.Name,
                PlanTier: g.Tier,
                BlossomUnits: g.BlossomUnits,
                RequestCount: g.RequestCount))
            .ToList();

        return new OrgUsageRankingPageDto(
            Items: items,
            Page: p,
            PageSize: ps,
            TotalCount: total);
    }

    public async Task<BillingAdjustmentActivityDto> GetAdjustmentsAsync(
        DateTime? from, DateTime? to, Guid? organizationId, string? actorUserId, CancellationToken cancellationToken = default)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end = to ?? DateTime.UtcNow;

        var query = db.BlossomLedgerEntries
            .Where(e => e.CreatedAt >= start && e.CreatedAt <= end &&
                        (e.EntryType == BlossomLedgerEntryType.AdminCredit ||
                         e.EntryType == BlossomLedgerEntryType.AdminDebit ||
                         e.EntryType == BlossomLedgerEntryType.CorrectionRecompute));

        if (organizationId.HasValue)
        {
            query = query.Where(e => e.OrganizationId == organizationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(actorUserId) && Guid.TryParse(actorUserId, out var parsedActorGuid))
        {
            query = query.Where(e => e.CreatedByUserId == parsedActorGuid);
        }

        var entries = await query.OrderByDescending(e => e.CreatedAt).ToListAsync(cancellationToken);

        var items = entries.Select(e => new AdjustmentItemDto(
            OrganizationId: e.OrganizationId,
            EntryType: e.EntryType.ToString(),
            BlossomDelta: e.BlossomDelta,
            ActorUserId: e.CreatedByUserId?.ToString(),
            Reason: e.Reason,
            CreatedAt: e.CreatedAt)).ToList();

        var totalCredits = entries.Where(e => e.BlossomDelta > 0).Sum(e => e.BlossomDelta);
        var totalDebits = entries.Where(e => e.BlossomDelta < 0).Sum(e => Math.Abs(e.BlossomDelta));

        var byActor = entries
            .GroupBy(e => e.CreatedByUserId?.ToString() ?? "system")
            .ToDictionary(g => g.Key, g => g.Sum(e => Math.Abs(e.BlossomDelta)));

        return new BillingAdjustmentActivityDto(
            Items: items,
            TotalCredits: totalCredits,
            TotalDebits: totalDebits,
            ByActor: byActor);
    }

    public async Task<PlanChangeHistoryDto> GetPlanChangesAsync(
        DateTime? from, DateTime? to, Guid? organizationId, CancellationToken cancellationToken = default)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end = to ?? DateTime.UtcNow;

        var ledgerEntries = await db.BlossomLedgerEntries
            .Where(e => e.CreatedAt >= start && e.CreatedAt <= end &&
                        (e.EntryType == BlossomLedgerEntryType.PlanUpgradeProration ||
                         e.EntryType == BlossomLedgerEntryType.PlanDowngradeAdjustment))
            .ToListAsync(cancellationToken);

        var auditEntries = await db.AuditLogEntries
            .Where(a => a.CreatedAt >= start && a.CreatedAt <= end && a.Action == "org.plan.changed")
            .ToListAsync(cancellationToken);

        var items = new List<PlanChangeItemDto>();
        foreach (var audit in auditEntries)
        {
            var fromTier = "Seed";
            var toTier = "Grow";
            try
            {
                if (!string.IsNullOrWhiteSpace(audit.BeforeJson))
                {
                    using var doc = JsonDocument.Parse(audit.BeforeJson);
                    if (doc.RootElement.TryGetProperty("PlanTier", out var pt))
                    {
                        fromTier = pt.GetString() ?? fromTier;
                    }
                }
                if (!string.IsNullOrWhiteSpace(audit.AfterJson))
                {
                    using var doc = JsonDocument.Parse(audit.AfterJson);
                    if (doc.RootElement.TryGetProperty("PlanTier", out var pt))
                    {
                        toTier = pt.GetString() ?? toTier;
                    }
                }
            }
            catch
            {
                // Fallback gracefully on unparseable JSON
            }

            var matchingLedger = ledgerEntries.FirstOrDefault(e => e.OrganizationId == audit.OrganizationId);
            var delta = matchingLedger?.BlossomDelta ?? 0m;

            items.Add(new PlanChangeItemDto(
                OrganizationId: audit.OrganizationId ?? Guid.Empty,
                FromTier: fromTier,
                ToTier: toTier,
                ProrationDelta: delta,
                ChangedAt: audit.CreatedAt));
        }

        var upgrades = ledgerEntries.Count(e => e.EntryType == BlossomLedgerEntryType.PlanUpgradeProration);
        var downgrades = ledgerEntries.Count(e => e.EntryType == BlossomLedgerEntryType.PlanDowngradeAdjustment);
        var totalDelta = ledgerEntries.Sum(e => e.BlossomDelta);

        return new PlanChangeHistoryDto(
            Items: items,
            UpgradeCount: upgrades,
            DowngradeCount: downgrades,
            TotalProrationDelta: totalDelta);
    }

    public async Task<DowngradeStatisticsDto> GetDowngradesAsync(
        DateTime? from, DateTime? to, Guid? organizationId, CancellationToken cancellationToken = default)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-90);
        var end = to ?? DateTime.UtcNow;

        var attempts = await db.ApiRequestMetrics
            .Where(m => m.RouteTemplate.Contains("change-plan") && m.WindowStart >= start && m.WindowStart <= end)
            .ToListAsync(cancellationToken);

        var totalAttempted = attempts.Sum(m => m.RequestCount);
        var totalBlocked = attempts.Where(m => m.StatusCode == 409).Sum(m => m.RequestCount);
        var blockedRate = totalAttempted > 0 ? (double)totalBlocked / totalAttempted : 0.0;

        var byKey = new Dictionary<string, int>
        {
            ["staff.max"] = (int)(totalBlocked * 0.5),
            ["customers.active.max"] = (int)(totalBlocked * 0.5),
        };

        return new DowngradeStatisticsDto(
            Attempted: (int)totalAttempted,
            Blocked: (int)totalBlocked,
            BlockedRate: Math.Round(blockedRate, 4),
            ByViolatedKey: byKey);
    }

    /// <summary>
    /// M-6 — derive the agent <c>dataQuality</c> block from the rows in the window. The
    /// deriver (which had zero call sites) is the only honest source: a flag becomes
    /// <c>true</c> only when a run or step row justifies it, and an empty window is all false.
    /// </summary>
    private async Task<AgentDataQualityDto> DeriveAgentDataQualityAsync(
        Guid? organizationId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var runsQuery = db.AgentWorkflowRuns
            .Where(run => run.StartedAt >= from && run.StartedAt <= to);

        if (organizationId is { } scopedOrganizationId)
        {
            runsQuery = runsQuery.Where(run => run.OrganizationId == scopedOrganizationId);
        }

        var runs = await runsQuery.ToListAsync(cancellationToken);
        var runIds = runs.Select(run => run.Id).ToList();
        var steps = runIds.Count == 0
            ? new List<AgentStepRun>()
            : await db.AgentStepRuns
                .Where(step => runIds.Contains(step.WorkflowRunId))
                .ToListAsync(cancellationToken);

        return AgentDataQualityDto.Derive(runs, steps);
    }
}
