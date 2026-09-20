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
/// The revenue journal's write surface. It is deliberately a single method: the ledger is
/// append-only, so there is no update and no delete to expose, and a void is expressed by the new
/// entry's <c>SupersedesEntryId</c>.
/// </summary>
public interface IIncomeLedgerService
{
    Task<IncomeLedgerEntry> RecordAsync(
        RecordIncomeCommand command, CancellationToken cancellationToken = default);
}
