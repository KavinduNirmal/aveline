using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;

namespace Aveline.Api.Modules.Payments.Repositories;

/// <summary>
/// Persistence for the provider-neutral payment intent. Reads that serve a tenant take the
/// organisation id, so tenant isolation fails closed at the repository rather than at the endpoint.
/// </summary>
public interface IPaymentIntentRepository
{
    /// <summary>An intent belonging to the organisation, or null when it does not exist for it.</summary>
    Task<PaymentIntent?> GetAsync(
        Guid organizationId, Guid intentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The intent that a provider charge or webhook refers to, resolved by the stored provider key
    /// so it remains readable after the configured provider changes (decision D5).
    /// </summary>
    Task<PaymentIntent?> FindByProviderIntentIdAsync(
        string provider, string providerIntentId, CancellationToken cancellationToken = default);

    /// <summary>The intent a previous request with the same client key already created, if any.</summary>
    Task<PaymentIntent?> FindByIdempotencyKeyAsync(
        Guid organizationId, PaymentPurpose purpose, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task AddAsync(PaymentIntent intent, CancellationToken cancellationToken = default);

    /// <summary>Persists a state transition on an existing intent.</summary>
    Task UpdateAsync(PaymentIntent intent, CancellationToken cancellationToken = default);
}
