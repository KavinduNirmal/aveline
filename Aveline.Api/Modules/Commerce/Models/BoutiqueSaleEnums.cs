namespace Aveline.Api.Modules.Commerce.Models;

/// <summary>
/// What a boutique sale entry records. The **sign is derived from this**, never stored: every
/// <see cref="BoutiqueSaleEntry.Amount"/> is positive, and a reader signs it by kind.
/// </summary>
/// <remarks>
/// The sign-as-a-function-of-kind rule is copied from
/// <see cref="Modules.Revenue.Models.IncomeEntryKind"/> and the Blossom ledger, for the same reason:
/// a signed money column invites a reader to sum a column that mixes what was taken with what was
/// returned, which is the exact mistake this ledger exists to prevent.
/// </remarks>
public enum BoutiqueSaleEntryKind
{
    /// <summary>+ A counter sale, or an order that reached a paid status.</summary>
    Sale,

    /// <summary>+ Money received against an order.</summary>
    PaymentReceived,

    /// <summary>− Money returned to a client.</summary>
    Refund,

    /// <summary>± An operator correction. Never something a sale asked for.</summary>
    Adjustment,
}

/// <summary>Which writer produced an entry, and therefore what its <c>SourceRef</c> names.</summary>
public enum BoutiqueSaleSourceKind
{
    /// <summary><c>SourceRef</c> is the customer interaction id.</summary>
    CounterWalkIn,

    /// <summary><c>SourceRef</c> is the payment id.</summary>
    OrderPayment,

    /// <summary><c>SourceRef</c> is the order id.</summary>
    OrderSettlement,

    /// <summary><c>SourceRef</c> is the refund reference.</summary>
    Refund,

    /// <summary>Written by a job or an unattributable path, with a deterministic reference.</summary>
    System,
}

/// <summary>
/// Whether an amount is something the shop expects to take, or something it confirmed it took.
/// </summary>
/// <remarks>
/// **This distinction is the ledger's honesty contract.** A <see cref="Derived"/> entry means "this
/// is what the order says was sold" — billed value with no evidence of collection. Only
/// <see cref="Verified"/> entries may be described as money taken. No screen in the tenant tree
/// presents <c>Derived + Verified</c> as one unlabelled figure.
/// </remarks>
public enum BoutiqueSaleChargeBasis
{
    /// <summary>What the order says was sold. Not evidence that money moved.</summary>
    Derived,

    /// <summary>A person, or the payment flow, asserted that this money was taken.</summary>
    Verified,
}

/// <summary>Lifecycle of an entry. There are exactly two states and no third.</summary>
public enum BoutiqueSaleEntryStatus
{
    /// <summary>Counts towards every total.</summary>
    Recorded,

    /// <summary>
    /// Superseded by another entry that points at it. Excluded from every total, but **never
    /// deleted** — the append-only rule means the original stays on disk.
    /// </summary>
    Voided,
}
