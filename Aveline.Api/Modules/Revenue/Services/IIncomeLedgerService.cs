using Aveline.Api.Modules.Revenue.Models;

namespace Aveline.Api.Modules.Revenue.Services;

/// <summary>
/// A request to append one entry to the revenue journal. Every value is explicit because this is
/// the only way money enters the ledger.
/// </summary>
public sealed record RecordIncomeCommand(
    Guid OrganizationId,
    decimal Amount,
    string Reason,
    IncomeEntryKind Kind,
    IncomeChargeBasis ChargeBasis,
    IncomeSourceKind SourceKind,
    string? SourceRef,
    DateTime? PeriodStart,
    DateTime? PeriodEnd,
    DateTime OccurredAt,
    Guid? RecordedByUserId,
    Guid? SupersedesEntryId = null,
    string? IdempotencyKey = null,
    string? IdempotencyScope = null);

/// <summary>
/// A request to record a <c>Verified</c> receipt. The service — not the caller — resolves the live
/// <c>Derived</c> expectation for the same <c>(SourceKind, SourceRef)</c> identity and takes it over
/// (plan §9.9, gap G12), so the webhook settlement and the admin route share one supersede path.
/// </summary>
/// <param name="Kind">The receipt's entry kind; the charge's own kind, never inferred.</param>
/// <param name="OccurredAt">When the receipt is booked, as the caller's clock reads it.</param>
/// <param name="RecordedByUserId">
/// The actor, or null for a system write (which <c>SourceKind = System</c> authorises).
/// </param>
/// <param name="PeriodStart">
/// The period the charge belongs to. Null (the admin verify route's case) means "take the
/// expectation's period"; a provider settlement supplies the period it persisted on the intent.
/// </param>
public sealed record VerifyIncomeCommand(
    Guid OrganizationId,
    decimal Amount,
    string Reason,
    IncomeEntryKind Kind,
    IncomeSourceKind SourceKind,
    string SourceRef,
    DateTime OccurredAt,
    Guid? RecordedByUserId,
    DateTime? PeriodStart = null,
    DateTime? PeriodEnd = null,
    string? IdempotencyKey = null,
    string? IdempotencyScope = null);

/// <summary>
/// The revenue journal's write surface. It exposes only append-only writes: the ledger is
/// append-only, so there is no update and no delete to expose, and a void is expressed by the new
/// entry's <c>SupersedesEntryId</c>.
/// </summary>
public interface IIncomeLedgerService
{
    Task<IncomeLedgerEntry> RecordAsync(
        RecordIncomeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a <c>Verified</c> receipt, taking over the live <c>Derived</c> expectation for the
    /// same <c>(SourceKind, SourceRef)</c> identity when one exists. The extraction is what lets a
    /// provider-settled charge replace the expectation the rollover wrote without either writer
    /// re-implementing the supersede rule (plan §9.9 item 1; risk R3).
    /// </summary>
    Task<IncomeLedgerEntry> VerifyAsync(
        VerifyIncomeCommand command, CancellationToken cancellationToken = default);
}
