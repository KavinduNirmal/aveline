using Aveline.Api.Modules.CustomerConcierge.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

/// <summary>Data access for <see cref="CustomerConsent"/>. One row per org + customer.</summary>
public interface ICustomerConsentRepository
{
    /// <summary>Returns the consent row for an org + customer, or null.</summary>
    Task<CustomerConsent?> GetForCustomerAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a consent row (defaults to <c>pending</c>).</summary>
    Task<CustomerConsent> AddAsync(CustomerConsent consent, CancellationToken cancellationToken = default);

    /// <summary>Persists a consent change.</summary>
    Task SaveAsync(CustomerConsent consent, CancellationToken cancellationToken = default);
}
