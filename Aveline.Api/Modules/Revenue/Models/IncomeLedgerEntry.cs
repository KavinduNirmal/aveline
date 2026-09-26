namespace Aveline.Api.Modules.Revenue.Models;

/// <summary>
/// One append-only entry in Aveline's own revenue journal.
/// </summary>
/// <remarks>
/// Modelled on <see cref="Modules.Billing.Models.BlossomLedgerEntry"/>, which is the only other
/// append-only money-shaped journal here: rows are never updated or deleted, a correction is a new
/// row, and a superseded row is marked <see cref="IncomeEntryStatus.Voided"/> rather than removed.
///
/// Three deliberate departures from that model:
///
/// 1. <see cref="Amount"/> is **always positive** and the sign comes from <see cref="Kind"/>, so a
///    column sum can never net a refund against a charge by accident.
/// 2. <see cref="ChargeBasis"/> exists, because this journal records both expectations and
///    receipts and conflating them would invent revenue.
/// 3. <see cref="Currency"/> is explicit even though only LKR is used today, because a revenue
///    number without a unit is not a measurement.
/// </remarks>
public sealed class IncomeLedgerEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public IncomeEntryKind Kind { get; set; }

    public IncomeSourceKind SourceKind { get; set; }

    /// <summary>
    /// What produced this entry, unique within its <see cref="SourceKind"/>. Together with
    /// <see cref="SourceKind"/> this is the dedup identity, enforced by a filtered unique index so
    /// a retried write cannot double-book.
    /// </summary>
    public string? SourceRef { get; set; }

    public IncomeChargeBasis ChargeBasis { get; set; }

    public IncomeEntryStatus Status { get; set; } = IncomeEntryStatus.Recorded;

    /// <summary>ISO-4217, e.g. <c>LKR</c>.</summary>
    public string Currency { get; set; } = "LKR";

    /// <summary>Always positive; the sign is derived from <see cref="Kind"/>.</summary>
    public decimal Amount { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary><c>null</c> for a top-up, a refund or an adjustment, which belong to no period.</summary>
    public DateTime? PeriodStart { get; set; }

    /// <summary>Exclusive, matching the billing period convention. <c>null</c> with the start.</summary>
    public DateTime? PeriodEnd { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who recorded this. Non-null by contract: an entry that cannot be attributed to a person is
    /// not journal-worthy, and the service refuses one.
    /// </summary>
    public Guid? RecordedByUserId { get; set; }

    /// <summary>The entry this one nulls, when it supersedes a derived expectation.</summary>
    public Guid? SupersedesEntryId { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? IdempotencyScope { get; set; }
}
