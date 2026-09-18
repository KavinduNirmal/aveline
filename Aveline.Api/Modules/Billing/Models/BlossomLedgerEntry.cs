namespace Aveline.Api.Modules.Billing.Models;

/// <summary>Sign of the entry is derived from this type (BR-2.1).</summary>
public enum BlossomLedgerEntryType
{
    /// <summary>+ The plan allowance for the period (written at period open).</summary>
    PeriodAllocation,

    /// <summary>+ A purchased or gifted Blossom pack.</summary>
    TopUpGrant,

    /// <summary>+ Manual credit by an Aveline administrator.</summary>
    AdminCredit,

    /// <summary>− Manual debit by an Aveline administrator.</summary>
    AdminDebit,

    /// <summary>− Reversal of a TopUpGrant.</summary>
    TopUpRevocation,

    /// <summary>− Expiry of an un-consumed, expiring grant.</summary>
    Expiry,

    /// <summary>+ Allowance delta on mid-period upgrade.</summary>
    PlanUpgradeProration,

    /// <summary>− Allowance delta on immediate downgrade.</summary>
    PlanDowngradeAdjustment,

    /// <summary>± Result of a pricing recompute.</summary>
    CorrectionRecompute,
}

/// <summary>Where a ledger entry originated.</summary>
public enum BlossomSourceKind
{
    Admin,
    PaymentProvider,
    PlanChange,
    Expiry,
    System,
}

/// <summary>
/// Append-only entitlement statement entry (domain-model.md §4.2). Rows are never updated
/// or deleted; corrections are new entries with <see cref="SupersedesEntryId"/> set.
/// </summary>
public sealed class BlossomLedgerEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public Guid UsageAccountId { get; set; }

    public BlossomLedgerEntryType EntryType { get; set; }

    /// <summary>Signed: positive credits, negative debits; never zero.</summary>
    public decimal BlossomDelta { get; set; }

    /// <summary>The remaining balance after this entry.</summary>
    public decimal BlossomBalanceAfter { get; set; }

    public string Reason { get; set; } = string.Empty;

    public BlossomSourceKind? SourceKind { get; set; }

    public string? SourceRef { get; set; }

    /// <summary>Set on a revocation to point at the grant being reversed.</summary>
    public Guid? SupersedesEntryId { get; set; }

    /// <summary><c>null</c> means the grant never expires.</summary>
    public DateTime? ExpiresAt { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? IdempotencyScope { get; set; }

    /// <summary><c>null</c> for system entries.</summary>
    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
