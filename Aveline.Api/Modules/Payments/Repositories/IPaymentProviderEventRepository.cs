using Aveline.Api.Modules.Payments.Models;

namespace Aveline.Api.Modules.Payments.Repositories;

/// <summary>
/// Persistence for the webhook inbox. The unique <c>(Provider, ProviderEventId)</c> index is the
/// replay guard, so <see cref="AddAsync"/> is where a duplicate is detected.
/// </summary>
public interface IPaymentProviderEventRepository
{
    /// <summary>The inbox row for a provider event, or null when it has not been seen.</summary>
    Task<PaymentProviderEvent?> FindByProviderEventIdAsync(
        string provider, string providerEventId, CancellationToken cancellationToken = default);

    /// <summary>The oldest unprocessed events, for a retry sweep.</summary>
    Task<IReadOnlyList<PaymentProviderEvent>> ListUnprocessedAsync(
        int limit, CancellationToken cancellationToken = default);

    Task AddAsync(PaymentProviderEvent providerEvent, CancellationToken cancellationToken = default);

    /// <summary>Records the dispatch outcome on an existing inbox row.</summary>
    Task UpdateAsync(PaymentProviderEvent providerEvent, CancellationToken cancellationToken = default);
}
