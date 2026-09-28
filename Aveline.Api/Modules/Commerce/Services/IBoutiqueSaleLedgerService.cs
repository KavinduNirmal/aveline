using Aveline.Api.Modules.Commerce.Models;

namespace Aveline.Api.Modules.Commerce.Services;

/// <summary>
/// A request to append one entry to the boutique's takings journal. Every value is explicit because
/// this is the only way money enters the ledger.
/// </summary>
public sealed record RecordBoutiqueSaleCommand(
    Guid OrganizationId,
    decimal Amount,
    string Reason,
    BoutiqueSaleEntryKind Kind,
    BoutiqueSaleChargeBasis ChargeBasis,
    BoutiqueSaleSourceKind SourceKind,
    string? SourceRef,
    DateTime OccurredAt,
    Guid? RecordedByUserId,
    Guid? OrderId = null,
    Guid? CustomerId = null,
    Guid? PaymentId = null,
    Guid? SupersedesEntryId = null,
    string? IdempotencyKey = null,
    string? IdempotencyScope = null);

/// <summary>
/// The boutique takings journal's write surface. Deliberately a single method: the ledger is
/// append-only, so there is no update and no delete to expose, and a correction is expressed by the
/// new entry's <c>SupersedesEntryId</c>.
/// </summary>
/// <remarks>
/// **There is no tenant-facing adjustment route and there will not be one in this slice.** Boutique
/// roles hold no moving-money permission by design, and a shop editing its own revenue journal is an
/// audit smell; a mis-keyed counter sale is corrected by the reconciliation job plus an
/// Aveline-team adjustment. This interface is called by the four product writers and by that job.
/// </remarks>
public interface IBoutiqueSaleLedgerService
{
    Task<BoutiqueSaleEntry> RecordAsync(
        RecordBoutiqueSaleCommand command, CancellationToken cancellationToken = default);
}
