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
}
