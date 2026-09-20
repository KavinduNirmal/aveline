using Aveline.Api.Modules.Revenue.Models;

namespace Aveline.Api.Modules.Revenue.DTOs;

/// <summary>
/// A verified receipt: money an Aveline operator (or, later, a provider) confirmed was received
/// against a reference the ledger already expects.
/// </summary>
/// <remarks>
/// The <see cref="SourceKind"/> and <see cref="SourceRef"/> pair is the dedup identity, so the
/// operator names the charge being settled rather than inventing one. The resulting entry nulls its
/// <see cref="IncomeChargeBasis.Derived"/> counterpart, which is what stops the same charge being
/// counted twice.
/// </remarks>
public sealed record VerifyIncomeRequest(
    Guid OrganizationId,
    IncomeSourceKind SourceKind,
    string SourceRef,
    decimal Amount,
    string Reason);

/// <summary>
/// A refund against a charge that was actually collected.
/// </summary>
/// <remarks>
/// Refusing to refund a <see cref="IncomeChargeBasis.Derived"/>-only charge is the point: you cannot
/// return money the ledger never recorded receiving, and recording it as a negative total would
/// hide that. The service answers <c>409 refund-not-allowed</c> for it.
/// </remarks>
public sealed record RefundIncomeRequest(
    Guid OrganizationId,
    IncomeSourceKind SourceKind,
    string SourceRef,
    decimal Amount,
    string Reason);

/// <summary>An operator correction. Corrects the ledger; never something a list price asked for.</summary>
public sealed record AdjustIncomeRequest(
    Guid OrganizationId,
    decimal Amount,
    string Reason,
    string? SourceRef = null,
    Guid? SupersedesEntryId = null);

/// <summary>The wire shape of one income ledger entry.</summary>
public sealed record IncomeLedgerEntryDto(
    Guid Id,
    Guid OrganizationId,
    string Kind,
    string SourceKind,
    string? SourceRef,
    string ChargeBasis,
    string Status,
    string Currency,
    decimal Amount,
    string Reason,
    DateTime? PeriodStart,
    DateTime? PeriodEnd,
    DateTime OccurredAt,
    Guid? RecordedByUserId,
    Guid? SupersedesEntryId)
{
    public static IncomeLedgerEntryDto From(IncomeLedgerEntry entry) => new(
        entry.Id,
        entry.OrganizationId,
        entry.Kind.ToString(),
        entry.SourceKind.ToString(),
        entry.SourceRef,
        entry.ChargeBasis.ToString(),
        entry.Status.ToString(),
        entry.Currency,
        entry.Amount,
        entry.Reason,
        entry.PeriodStart,
        entry.PeriodEnd,
        entry.OccurredAt,
        entry.RecordedByUserId,
        entry.SupersedesEntryId);
}
