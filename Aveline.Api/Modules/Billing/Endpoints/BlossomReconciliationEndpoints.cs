using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Endpoints;

/// <summary>
/// S-56 — the cross-org Blossom reconciliation read.
/// </summary>
/// <remarks>
/// Drift is already a Critical alert: `SystemMetricCollector` emits
/// `aveline.blossom.reconciliation.drift` over the accounts it scans, and `blossom.ledger.drift`
/// watches it (S-3, S-30). An alarm is only actionable if the operator can find *which* account
/// drifted, and until this endpoint the only place to look was Grafana.
///
/// The formula is deliberately **not** reimplemented here: it calls the same
/// <see cref="BlossomService.LedgerDerivedBalance"/> and <see cref="BlossomService.ReconciliationDrift"/>
/// the collector does. A second derivation would let the console and the alarm disagree about the
/// same account, which is the one outcome this surface must never produce.
/// </remarks>
public static class BlossomReconciliationEndpoints
{
    public const string Tag = "Admin Billing Statistics";

    public static IEndpointRouteBuilder MapBlossomReconciliationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        // Same policy as the rest of `/admin/statistics/billing`: `stats:system`, team-only and
        // bearer-only. Deliberately *not* `revenue:read` — a moderator reads revenue and does not
        // read system statistics, and this is the latter.
        // The full `/api/v1` prefix: `BillingModule.MapBillingEndpoints` is registered at the app
        // root, not inside the `v1` group, because the sibling `/internal/usage` routes deliberately
        // live outside the versioned surface.
        var group = endpoints.MapGroup("/api/v1/admin/statistics/billing")
            .WithTags(Tag)
            .RequireAuthorization(AuthorizationConfiguration.StatsSystemPolicy);

        group.MapGet("/reconciliation", GetReconciliationAsync)
            .WithName("getBillingReconciliation")
            .WithSummary("Read per-account Blossom reconciliation drift")
            .WithDescription(
                "S-56. Every open account whose projected balance disagrees with its ledger, ranked by "
                + "the magnitude of the drift so the worst is first. Uses the same formula "
                + "`SystemMetricCollector` emits for the `blossom.ledger.drift` alert. "
                + "Requires `stats:system`.")
            .Produces<BlossomReconciliationDto>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> GetReconciliationAsync(
        [FromQuery] Guid? organizationId,
        AppDbContext db,
        CancellationToken ct)
    {
        // An optional organization scope: the global report answers "is anything wrong", and the
        // scoped one answers "is *this* boutique wrong", which is the next question an operator asks.
        var accounts = await db.UsageAccounts
            .Where(account => !account.IsClosed)
            .Where(account => organizationId == null || account.OrganizationId == organizationId)
            .ToListAsync(ct);

        if (accounts.Count == 0)
        {
            return Results.Ok(Empty(organizationId));
        }

        var accountIds = accounts.Select(account => account.Id).ToArray();

        // One grouped read rather than one per account: the same reason the statement batches its
        // revocable-amount lookup.
        var nonAllocationByAccount = await db.BlossomLedgerEntries
            .Where(entry => accountIds.Contains(entry.UsageAccountId))
            .Where(entry => entry.EntryType != BlossomLedgerEntryType.PeriodAllocation)
            .GroupBy(entry => entry.UsageAccountId)
            .Select(group => new { AccountId = group.Key, Total = group.Sum(entry => entry.BlossomDelta) })
            .ToDictionaryAsync(row => row.AccountId, row => row.Total, ct);

        var names = await db.Organizations
            .ToDictionaryAsync(org => org.Id, org => org.Name, ct);

        var rows = accounts
            .Select(account =>
            {
                var deltas = nonAllocationByAccount.GetValueOrDefault(account.Id);
                return new BlossomReconciliationRowDto(
                    account.OrganizationId,
                    names.GetValueOrDefault(account.OrganizationId, "Unknown organization"),
                    account.PeriodStart,
                    account.BlossomRemaining,
                    BlossomService.LedgerDerivedBalance(account, deltas),
                    BlossomService.ReconciliationDrift(account, deltas));
            })
            .Where(row => row.Drift != 0m)
            // Worst first, by magnitude: a large over-credit is as wrong as a large under-credit, and
            // signing the sort would bury one class behind the other.
            .OrderByDescending(row => Math.Abs(row.Drift))
            .ToArray();

        return Results.Ok(new BlossomReconciliationDto(
            OrganizationId: organizationId,
            Accounts: rows,
            DriftedCount: rows.Length,
            AccountsChecked: accounts.Count,
            // `null`, not `false`. This read does not run the collector, so it does not know whether
            // a full reconciliation pass has happened; saying `false` would claim it checked and
            // found nothing, which is a different and unearned statement.
            ReconciliationChecked: null,
            CheckedAt: DateTime.UtcNow,
            Notes:
            [
                "Drift is computed with the same formula SystemMetricCollector emits for the "
                + "blossom.ledger.drift alert, so the console and the alarm cannot disagree. A "
                + "non-zero drift is a Critical condition: the projected balance and the ledger "
                + "disagree.",
                "This read does not itself run a reconciliation pass, so it does not report whether "
                + "one has happened.",
            ]));
    }

    private static BlossomReconciliationDto Empty(Guid? organizationId) => new(
        OrganizationId: organizationId,
        Accounts: [],
        DriftedCount: 0,
        AccountsChecked: 0,
        ReconciliationChecked: null,
        CheckedAt: DateTime.UtcNow,
        Notes:
        [
            "No open Blossom accounts exist, so there is nothing to reconcile.",
        ]);
}

/// <summary>One account whose projection disagrees with its ledger.</summary>
public sealed record BlossomReconciliationRowDto(
    Guid OrganizationId,
    string OrganizationName,
    DateTime PeriodStart,
    decimal ProjectedBalance,
    decimal LedgerDerivedBalance,
    decimal Drift)
{
    /// <summary>Drift is the whole reason this read exists, so it is never consistent by definition.</summary>
    public bool IsConsistent => Drift == 0m;
}

/// <summary>The cross-org reconciliation report (S-56).</summary>
public sealed record BlossomReconciliationDto(
    /// <summary>The scope this report covers, or `null` for every open account.</summary>
    Guid? OrganizationId,
    IReadOnlyList<BlossomReconciliationRowDto> Accounts,
    int DriftedCount,
    int AccountsChecked,
    /// <summary>
    /// `null` when this read did not run a reconciliation pass, which is always the case here. Never
    /// `false`, which would claim it checked and found nothing.
    /// </summary>
    bool? ReconciliationChecked,
    DateTime CheckedAt,
    IReadOnlyList<string> Notes);
