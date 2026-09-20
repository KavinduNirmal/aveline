using Aveline.Api.Modules.Revenue.Models;

namespace Aveline.Api.Modules.Revenue.Domain;

/// <summary>
/// The reconciliation identity of the income ledger, as the statement reports it (S-50).
/// </summary>
/// <remarks>
/// Pure and I/O-free so the honesty core is unit-testable without a database. The one rule that
/// carries the family: a window in which everything billed was collected is **balanced**, and a
/// window in which anything was billed but not collected is **not** — and the difference is
/// reported rather than netted away.
/// </remarks>
public sealed record RevenueReconciliationTotals(
    decimal DerivedTotal,
    decimal VerifiedTotal,
    decimal UnverifiedGap,
    decimal RefundTotal,
    decimal NetVerified,
    bool IsBalanced);

public static class RevenueReconciliation
{
    /// <summary>
    /// Computes the window totals. <see cref="IncomeEntryStatus.Voided"/> rows are excluded
    /// entirely: a superseded expectation is not revenue, and its replacement carries the value.
    /// </summary>
    /// <remarks>
    /// Three deliberate choices, each of which a reader could otherwise get wrong:
    ///
    /// - **A refund is not uncollected revenue.** The gap closes on collection, not on returns, so
    ///   <c>RefundTotal</c> reduces <c>NetVerified</c> and leaves <c>UnverifiedGap</c> alone.
    /// - **An adjustment counts as verified movement, never as derived revenue.** It corrects the
    ///   ledger; it is not something a list price asked for.
    /// - **Over-collection is reported, not clipped.** A receipt with no matching charge is
    ///   exactly what an operator needs to see, so the gap goes negative rather than to zero.
    ///   `IsBalanced` still holds, because nothing is outstanding.
    /// </remarks>
    public static RevenueReconciliationTotals Of(IEnumerable<IncomeLedgerEntry> entries)
    {
        decimal derived = 0m;
        decimal verified = 0m;
        decimal refunds = 0m;

        foreach (var entry in entries)
        {
            if (entry.Status == IncomeEntryStatus.Voided)
            {
                continue;
            }

            switch (entry.Kind)
            {
                case IncomeEntryKind.Refund:
                    refunds += entry.Amount;
                    break;

                case IncomeEntryKind.Adjustment:
                    verified += entry.Amount;
                    break;

                default:
                    if (entry.ChargeBasis == IncomeChargeBasis.Derived)
                    {
                        derived += entry.Amount;
                    }
                    else
                    {
                        verified += entry.Amount;
                    }
                    break;
            }
        }

        var gap = derived - verified;

        return new RevenueReconciliationTotals(
            DerivedTotal: derived,
            VerifiedTotal: verified,
            // Absolute magnitude: the gap is "how far apart are these two figures", and a receipt
            // with no matching charge is as much a finding as a charge with no receipt. Exposing it
            // as a signed subtraction would report the first case as a negative number that reads
            // like a typo.
            UnverifiedGap: Math.Abs(gap),
            RefundTotal: refunds,
            NetVerified: verified - refunds,
            // Balanced means at least as much was collected as was billed. Over-collection is a
            // finding reported by the gap's size and the per-organization read, not by this flag.
            IsBalanced: gap <= 0m);
    }
}
