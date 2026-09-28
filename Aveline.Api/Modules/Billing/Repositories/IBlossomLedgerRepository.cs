using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Repositories;

/// <summary>
/// Persistence for the append-only Blossom ledger and the balance projection. There is
/// deliberately no update or delete method for ledger entries.
/// </summary>
public interface IBlossomLedgerRepository
{
    Task<UsageAccount?> GetAccountAsync(
        Guid organizationId, DateTime periodStart, CancellationToken cancellationToken = default);

    /// <summary>Replays the freshest row values (including the concurrency token) after a conflict.</summary>
    Task<UsageAccount?> ReloadAccountAsync(
        UsageAccount account, CancellationToken cancellationToken = default);

    /// <summary>Replay lookup for an idempotent ledger write (BR-2.8).</summary>
    Task<BlossomLedgerEntry?> FindEntryByKeyAsync(
        Guid organizationId, string idempotencyScope, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<BlossomLedgerEntry?> GetEntryAsync(Guid entryId, CancellationToken cancellationToken = default);

    /// <summary>Persists the entry and the updated projection in one transaction (FR-2.1).</summary>
    Task AddEntryAndUpdateAccountAsync(
        BlossomLedgerEntry entry, UsageAccount account, CancellationToken cancellationToken = default);

    /// <summary>Amount already revoked from a grant.</summary>
    Task<decimal> GetRevokedAmountAsync(Guid grantEntryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BlossomLedgerEntry>> ListEntriesAsync(
        Guid organizationId, DateTime? from, DateTime? to, BlossomLedgerEntryType? entryType,
        int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> CountEntriesAsync(
        Guid organizationId, DateTime? from, DateTime? to, BlossomLedgerEntryType? entryType,
        CancellationToken cancellationToken = default);

    /// <summary>Sum of all ledger deltas for a period; used by the reconciliation check.</summary>
    Task<decimal> SumDeltasAsync(Guid usageAccountId, CancellationToken cancellationToken = default);

    /// <summary>Sum of ledger deltas excluding one entry type (used to reconcile the allowance).</summary>
    Task<decimal> SumDeltasExcludingAsync(
        Guid usageAccountId, BlossomLedgerEntryType excludedType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the merged entitlement ∪ consumption statement, ordered `OccurredAt DESC,
    /// Id DESC`, with the true window total.
    /// </summary>
    /// <remarks>
    /// The merge, the filter, the ordering and the paging all happen in the database. The delivered
    /// statement read the whole window and paged in memory, which is what this replaces.
    /// </remarks>
    Task<(int Total, IReadOnlyList<BlossomStatementRow> Rows)> ListStatementPageAsync(
        Guid organizationId, BlossomStatementFilter filter, int page, int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Sum of every ledger delta in a window, used to derive the opening balance.</summary>
    Task<decimal> SumDeltasInWindowAsync(
        Guid organizationId, DateTime from, DateTime to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The **net balance movement** of everything the filter matched before the given page offset,
    /// so a running balance is correct on every page. Ledger deltas are signed; consumption is
    /// negated because it reduces the balance.
    /// </summary>
    Task<decimal> SumStatementMovementBeforeAsync(
        Guid organizationId, BlossomStatementFilter filter, int rowsToSkip,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The still-revocable amount for each of the given grant entries, keyed by entry id. A grant
    /// absent from the result is not revocable at all — wrong type, non-positive, expired, or
    /// already fully revoked — which is the same rule <c>RevokeAsync</c> enforces.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetRevocableAmountsAsync(
        IReadOnlyCollection<Guid> grantEntryIds, CancellationToken cancellationToken = default);
}

/// <summary>How the statement's merged source is narrowed, applied in the database.</summary>
/// <remarks>
/// A record rather than a parameter list on every helper, so the three paging methods share one
/// definition of "what the caller asked for" and cannot drift apart.
/// </remarks>
public sealed record BlossomStatementFilter(
    DateTime From,
    DateTime To,
    bool IncludeEntitlements,
    bool IncludeConsumption,
    BlossomLedgerEntryType? EntryType = null,
    BlossomSourceKind? SourceKind = null,
    string? Query = null,
    decimal? MinAmount = null,
    decimal? MaxAmount = null);

/// <summary>
/// One row of the merged statement, flattened so both sources share a shape the database can order
/// and page as one.
/// </summary>
/// <remarks>
/// <see cref="IsEntitlement"/> is the discriminator, and the consumption-only columns are null on a
/// ledger row (and vice versa). Only the page's rows are hydrated to full entities: the filters that
/// need them (the amount bounds and the stable tie-break) are evaluated in SQL.
/// </remarks>
public sealed record BlossomStatementRow(
    Guid Id,
    DateTime OccurredAt,
    bool IsEntitlement,
    decimal BlossomDelta,
    BlossomLedgerEntryType? EntryType,
    string Reason,
    BlossomSourceKind? SourceKind,
    string? SourceRef,
    DateTime? ExpiresAt,
    Guid? CreatedByUserId,
    decimal BlossomUnits,
    string? Provider,
    string? Model,
    long? NormalizedUnits,
    decimal? ActualCostUsd);
