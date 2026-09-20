namespace Aveline.Api.Modules.Revenue.Models;

/// <summary>
/// What an income entry records. The **sign is derived from this**, never stored: every
/// <see cref="IncomeLedgerEntry.Amount"/> is positive, and a reader signs it by kind.
/// </summary>
/// <remarks>
/// This is the one place the revenue ledger deliberately differs from the Blossom ledger, whose
/// `BlossomDelta` is signed. The reason is that a signed money column invites a reader to sum a
/// column that mixes what was billed with what was returned, which is the single mistake this
/// ledger exists to prevent.
/// </remarks>
public enum IncomeEntryKind
{
    /// <summary>+ What a subscription period's list price says should be billed.</summary>
    SubscriptionCharge,

    /// <summary>+ A Blossom top-up pack sold for a stated payment reference.</summary>
    TopUpPurchase,

    /// <summary>− Money returned to a customer.</summary>
    Refund,

    /// <summary>± An operator correction. Never something the list price asked for.</summary>
    Adjustment,
}

/// <summary>Which subsystem produced an entry, and therefore what its <c>SourceRef</c> names.</summary>
public enum IncomeSourceKind
{
    /// <summary><c>SourceRef</c> is the billing period start, ISO-8601.</summary>
    SubscriptionBilling,

    /// <summary><c>SourceRef</c> is the provider payment reference given at top-up.</summary>
    BlossomTopUp,

    /// <summary><c>SourceRef</c> is an operator-supplied reference.</summary>
    Admin,

    /// <summary>Written by a job, with a deterministic reference.</summary>
    System,
}

/// <summary>
/// Whether an amount is something we expect to be paid, or something an operator confirmed was.
/// </summary>
/// <remarks>
/// **This distinction is the ledger's honesty contract.** There is no payment-provider client in
/// this repository, so a top-up grant is not a charge and a period boundary is not a receipt; see
/// <c>docs/api/README.md</c> §C.2. A <see cref="Derived"/> entry is an expectation. Only
/// <see cref="Verified"/> entries may be described as collected money.
/// </remarks>
public enum IncomeChargeBasis
{
    /// <summary>What the list price says should be billed. Not a receipt.</summary>
    Derived,

    /// <summary>Money an operator, or later a provider, confirmed was received.</summary>
    Verified,
}

/// <summary>Lifecycle of an entry. There are exactly two states and no third.</summary>
public enum IncomeEntryStatus
{
    /// <summary>Counts towards every total.</summary>
    Recorded,

    /// <summary>
    /// Superseded by another entry that points at it. Excluded from every total, but **never
    /// deleted** — the append-only rule means the original stays on disk.
    /// </summary>
    Voided,
}
