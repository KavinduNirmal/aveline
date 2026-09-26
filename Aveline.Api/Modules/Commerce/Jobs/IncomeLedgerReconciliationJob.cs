using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Commerce.Jobs;

/// <summary>
/// Repairs the boutique income ledger when a business event succeeded but its ledger write did not.
/// </summary>
/// <remarks>
/// <para>
/// A payment confirmation returns <c>200</c> because the money moved. If the ledger insert failed
/// after that — a transient database error, a process restart — the register silently understates
/// the shop's takings, and nothing else in the system will notice. This job is the repair.
/// </para>
/// <para>
/// **It is idempotent by construction.** Every repair uses the same `SourceRef` the product writer
/// would have used, and the ledger's filtered unique index on
/// `(OrganizationId, SourceKind, SourceRef)` makes a second attempt a no-op rather than a duplicate
/// row. That is what makes a backfill safe to run on a cadence, and why the backfill is a job and
/// not a migration.
/// </para>
/// <para>
/// **It reports how many rows it repaired, and that count is the point.** A job that quietly repairs
/// rows on every run is telling an operator that a writer upstream is broken; returning a bare
/// success would hide exactly the signal that matters.
/// </para>
/// <para>
/// **What it cannot repair, and says so rather than guessing.** The plan specified this job as
/// repairing "confirmed payments **or** interactions carrying a purchase total". Writing it
/// established that the second half is impossible: <c>CustomerInteraction</c> has **no amount
/// column**. `CustomerVisitService` reads the purchase total from the request, folds it into
/// `Customer.TotalSpent`, and discards it — nothing on the row records what was taken. The only
/// available "repair" would be to invent an amount, and a fabricated figure in a money journal is
/// worse than a known gap. The honest fix is a migration that persists the amount on the
/// interaction; until then this job repairs payments and counts what it could not.
/// </para>
/// </remarks>
public sealed class IncomeLedgerReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<IncomeLedgerReconciliationJob> logger) : BackgroundService
{
    /// <summary>The name the distributed lock is keyed on.</summary>
    public const string Name = "income-ledger-reconciliation";

    /// <summary>
    /// How far back a repair pass looks. Bounded on purpose: this is a repair for a recent failure,
    /// not a historical backfill, and the ledger's `incomeLedgerBackfilled` flag is what tells a
    /// reader where the ledger begins.
    /// </summary>
    public static readonly TimeSpan RepairWindow = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunLockedAsync(stoppingToken);

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunLockedAsync(CancellationToken stoppingToken)
    {
        await using var handle = await jobLock.TryAcquireAsync(Name, cancellationToken: stoppingToken);
        if (handle is null)
        {
            return;
        }

        try
        {
            var repaired = await RunAsync(stoppingToken);
            if (repaired > 0)
            {
                // Logged at warning, not information: a repair means a writer upstream failed, and
                // that is a signal an operator needs rather than a routine success.
                logger.LogWarning(
                    "Income ledger reconciliation repaired {Count} missing entry(ies) in the last {Days} days. "
                    + "A persistent non-zero count indicates a failing ledger writer.",
                    repaired, RepairWindow.TotalDays);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Income ledger reconciliation failed.");
        }
    }

    /// <summary>Performs one repair pass and returns how many rows were appended.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<IBoutiqueSaleLedgerService>();

        var since = DateTime.UtcNow - RepairWindow;
        var repaired = 0;

        // ── confirmed payments missing a `PaymentReceived` row ───────────────────────────────────
        var confirmed = await db.Payments
            .AsNoTracking()
            .Where(payment => payment.Status == "confirmed"
                              && payment.ConfirmedAt != null
                              && payment.ConfirmedAt >= since)
            .Select(payment => new
            {
                payment.Id,
                payment.OrganizationId,
                payment.OrderId,
                payment.Amount,
                payment.ConfirmedAt,
            })
            .ToListAsync(cancellationToken);

        if (confirmed.Count > 0)
        {
            var candidateRefs = confirmed.Select(payment => $"payment:{payment.Id}").ToList();
            var alreadyRecorded = await db.BoutiqueSaleEntries
                .AsNoTracking()
                .Where(entry => entry.SourceKind == BoutiqueSaleSourceKind.OrderPayment
                                && entry.SourceRef != null
                                && candidateRefs.Contains(entry.SourceRef))
                .Select(entry => entry.SourceRef!)
                .ToListAsync(cancellationToken);

            var recorded = new HashSet<string>(alreadyRecorded, StringComparer.Ordinal);

            foreach (var payment in confirmed)
            {
                var sourceRef = $"payment:{payment.Id}";
                if (recorded.Contains(sourceRef))
                {
                    continue;
                }

                // `OrderPayment` is the same source kind the product writer uses and the same
                // `SourceRef`, so a race between this job and a late writer resolves to one row
                // rather than two: both would collide on the filtered unique index.
                await ledger.RecordAsync(new RecordBoutiqueSaleCommand(
                    OrganizationId: payment.OrganizationId,
                    Amount: payment.Amount,
                    Reason: BoutiqueIncomeReadService.ReconciliationReasonPrefix
                        + $" payment {payment.Id} was confirmed with no ledger entry.",
                    Kind: BoutiqueSaleEntryKind.PaymentReceived,
                    ChargeBasis: BoutiqueSaleChargeBasis.Verified,
                    SourceKind: BoutiqueSaleSourceKind.OrderPayment,
                    SourceRef: sourceRef,
                    // The occurrence is when the money was confirmed, not when the repair ran, so a
                    // repaired row lands in the right period of the register.
                    OccurredAt: payment.ConfirmedAt ?? DateTime.UtcNow,
                    RecordedByUserId: null,
                    OrderId: payment.OrderId,
                    PaymentId: payment.Id), cancellationToken);

                recorded.Add(sourceRef);
                repaired++;
            }
        }

        // ── counter sales the repair cannot honestly reconstruct ─────────────────────────────────
        //
        // Counted and logged, never written. `CustomerInteraction` has no amount column, so an
        // interaction that took money leaves no recoverable figure. This is the one place the job
        // knowingly leaves a gap, and an operator sees the count rather than a silent nothing.
        var unrepairable = await db.CustomerInteractions
            .AsNoTracking()
            .Where(interaction => interaction.OrganizationId != Guid.Empty
                                  && interaction.CreatedAt >= since)
            .CountAsync(cancellationToken);

        if (unrepairable > 0)
        {
            var recoverable = await db.BoutiqueSaleEntries
                .AsNoTracking()
                .CountAsync(entry => entry.SourceKind == BoutiqueSaleSourceKind.CounterWalkIn
                                     && entry.OccurredAt >= since, cancellationToken);

            if (unrepairable > recoverable)
            {
                logger.LogWarning(
                    "Income ledger reconciliation found {Unrepaired} recent interaction(s) with no "
                    + "CounterWalkIn ledger row ({Recovered} recovered). Counter sales cannot be "
                    + "reconstructed because CustomerInteraction persists no amount; see the "
                    + "reconciliation job's remarks.",
                    unrepairable - recoverable, recoverable);
            }
        }

        return repaired;
    }
}
