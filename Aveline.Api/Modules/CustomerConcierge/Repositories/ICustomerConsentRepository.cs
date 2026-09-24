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

    /// <summary>
    /// Atomically claims the one-time first-contact disclosure slot (plan §4.4 step 1). The claim is
    /// a conditional update - <c>SET DisclosureShownAt = shownAt WHERE ... AND DisclosureShownAt IS
    /// NULL</c> - so it is the concurrency control: the first caller gets <c>true</c>, every other
    /// caller (a concurrent inbound message, or a replay) gets <c>false</c> and must not send.
    /// </summary>
    /// <returns><c>true</c> only when this call set the stamp.</returns>
    Task<bool> TryClaimDisclosureAsync(
        Guid orgId,
        Guid customerId,
        string disclosureVersion,
        DateTime shownAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a claim that did not result in a delivered disclosure, so the next inbound message
    /// retries (plan §4.4 step 5). Clears both the timestamp and the version.
    /// </summary>
    Task ReleaseDisclosureAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default);
}
