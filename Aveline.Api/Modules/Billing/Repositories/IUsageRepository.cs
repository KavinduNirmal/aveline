using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Repositories;

public interface IUsageRepository
{
    /// <summary>
    /// Persists a new <see cref="AiUsageRecord"/> and atomically increments
    /// <see cref="UsageAccount.BlossomUsed"/> on the matching period ledger row.
    /// Both operations execute within a single database transaction.
    /// </summary>
    Task AddUsageRecordAndUpdateAccountAsync(
        AiUsageRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the <see cref="UsageAccount"/> for the given organisation and period,
    /// creating one with the supplied defaults if it does not yet exist.
    /// </summary>
    Task<UsageAccount> GetOrCreateAccountAsync(
        Guid organizationId,
        DateTime periodStart,
        DateTime periodEnd,
        decimal defaultBlossomLimit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paginated list of <see cref="AiUsageRecord"/> rows for an organisation,
    /// ordered newest-first.
    /// </summary>
    Task<IReadOnlyList<AiUsageRecord>> GetUsageRecordsAsync(
        Guid organizationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
