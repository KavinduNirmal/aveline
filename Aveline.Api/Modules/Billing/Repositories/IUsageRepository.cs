using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Repositories;

public interface IUsageRepository
{
    /// <summary>
    /// Persists a new <see cref="AiUsageRecord"/> and atomically increments
    /// <see cref="UsageAccount.BlossomUsed"/> on the matching period ledger row.
    /// Both operations execute within a single database transaction.
    /// </summary>
    /// <param name="record">The immutable usage record to insert.</param>
    /// <param name="defaultBlossomLimit">
    /// Plan allowance used only when the period row does not exist yet; callers resolve it
    /// from <see cref="Domain.IEntitlementResolver"/> rather than a hardcoded map.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddUsageRecordAndUpdateAccountAsync(
        AiUsageRecord record,
        decimal defaultBlossomLimit,
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

    /// <summary>Returns every usage record for an organisation inside a UTC window.</summary>
    Task<IReadOnlyList<AiUsageRecord>> ListRecordsInWindowAsync(
        Guid organizationId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Total Blossoms consumed in a UTC window, used to derive the statement's opening balance
    /// (Revenue Ledger R4). An aggregate rather than a row list, so the statement never has to
    /// materialise a whole window's consumption to compute one figure.
    /// </summary>
    Task<decimal> SumBlossomUnitsInWindowAsync(
        Guid organizationId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);
}
