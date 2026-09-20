using Aveline.Api.Modules.Revenue.Domain;
using Aveline.Api.Modules.Revenue.Models;

namespace Aveline.Api.Tests;

/// <summary>
/// Revenue Ledger R1 (issue #342) — the reconciliation identity, pure and I/O-free.
///
/// This is the honesty core of the family. The one rule that matters: a window in which
/// everything billed was actually collected is **balanced**, and a window in which anything was
/// billed but not collected is **not** — and the difference is reported rather than netted away.
/// </summary>
public class RevenueReconciliationTests
{
    private static IncomeLedgerEntry Entry(
        IncomeEntryKind kind,
        IncomeChargeBasis basis,
        decimal amount,
        IncomeEntryStatus status = IncomeEntryStatus.Recorded) => new()
        {
            OrganizationId = Guid.CreateVersion7(),
            Kind = kind,
            SourceKind = kind == IncomeEntryKind.TopUpPurchase
                ? IncomeSourceKind.BlossomTopUp
                : IncomeSourceKind.SubscriptionBilling,
            SourceRef = Guid.NewGuid().ToString("N"),
            ChargeBasis = basis,
            Status = status,
            Amount = amount,
            Reason = "A reason long enough to satisfy the rule.",
            OccurredAt = DateTime.UtcNow,
        };

    [Fact]
    public void EmptyWindow_IsAllZerosAndBalanced()
    {
        var result = RevenueReconciliation.Of([]);

        Assert.Equal(0m, result.DerivedTotal);
        Assert.Equal(0m, result.VerifiedTotal);
        Assert.Equal(0m, result.UnverifiedGap);
        Assert.Equal(0m, result.RefundTotal);
        Assert.Equal(0m, result.NetVerified);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public void DerivedOnly_ReportsTheWholeAmountAsTheUnverifiedGap()
    {
        var result = RevenueReconciliation.Of([
            Entry(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Derived, 5000m),
            Entry(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Derived, 2500m),
        ]);

        Assert.Equal(7500m, result.DerivedTotal);
        Assert.Equal(0m, result.VerifiedTotal);
        // The gap is the point of the ledger: not zero, and not an error.
        Assert.Equal(7500m, result.UnverifiedGap);
        Assert.False(result.IsBalanced);
    }

    [Fact]
    public void FullyVerifiedWindow_IsBalanced()
    {
        var result = RevenueReconciliation.Of([
            Entry(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Derived, 5000m),
            Entry(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Verified, 5000m),
        ]);

        Assert.Equal(5000m, result.DerivedTotal);
        Assert.Equal(5000m, result.VerifiedTotal);
        Assert.Equal(0m, result.UnverifiedGap);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public void PartialCollection_ReportsTheOutstandingGap()
    {
        var result = RevenueReconciliation.Of([
            Entry(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Derived, 5000m),
            Entry(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Verified, 2000m),
        ]);

        Assert.Equal(3000m, result.UnverifiedGap);
        Assert.False(result.IsBalanced);
    }

    [Fact]
    public void Refunds_ReduceNetVerified_AndNeverVerifiedTotal()
    {
        var result = RevenueReconciliation.Of([
            Entry(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Derived, 5000m),
            Entry(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Verified, 5000m),
            Entry(IncomeEntryKind.Refund, IncomeChargeBasis.Verified, 1500m),
        ]);

        Assert.Equal(5000m, result.VerifiedTotal);
        Assert.Equal(1500m, result.RefundTotal);
        Assert.Equal(3500m, result.NetVerified);
        // A refund is not uncollected revenue: the gap closes on collection, not on returns.
        Assert.Equal(0m, result.UnverifiedGap);
    }

    [Fact]
    public void Voided_EntriesAreIgnoredEntirely()
    {
        var result = RevenueReconciliation.Of([
            Entry(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Derived, 5000m),
            Entry(
                IncomeEntryKind.SubscriptionCharge,
                IncomeChargeBasis.Derived,
                9999m,
                IncomeEntryStatus.Voided),
        ]);

        Assert.Equal(5000m, result.DerivedTotal);
    }

    [Fact]
    public void AnAdjustment_CountsAsVerifiedMovement_NotAsDerivedRevenue()
    {
        var result = RevenueReconciliation.Of([
            Entry(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Derived, 5000m),
            Entry(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Verified, 5000m),
            Entry(IncomeEntryKind.Adjustment, IncomeChargeBasis.Verified, 250m),
        ]);

        // An adjustment corrects the ledger; it is not something the list price asked for.
        Assert.Equal(5000m, result.DerivedTotal);
        Assert.Equal(5250m, result.VerifiedTotal);
        Assert.True(result.IsBalanced);
    }

    /// <summary>
    /// Over-collection is reported rather than clipped — but as a **magnitude**. The gap answers
    /// "how far apart are these two figures", and a receipt with no matching charge is as much a
    /// finding as a charge with no receipt, so it is not signed. The signed view is
    /// `collectionRate.outstanding`, which is a balance and where a negative value means something.
    /// </summary>
    [Fact]
    public void OverCollection_IsReportedAsAMagnitude_RatherThanClipped()
    {
        var result = RevenueReconciliation.Of([
            Entry(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Derived, 1000m),
            Entry(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Verified, 1400m),
        ]);

        Assert.Equal(400m, result.UnverifiedGap);
        Assert.True(result.IsBalanced);
    }
}
