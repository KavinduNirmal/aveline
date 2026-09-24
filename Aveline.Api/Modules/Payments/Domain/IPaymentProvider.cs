namespace Aveline.Api.Modules.Payments.Domain;

/// <summary>
/// The provider-agnostic payment SPI (plan §6.2). Every adapter (manual, mock, and later an
/// external provider) implements exactly this interface, and no type above it may reference a
/// provider SDK: the adapter types live in <c>Providers/</c> and are internal to the module, so a
/// provider SDK type cannot leak past the module boundary.
/// </summary>
/// <remarks>
/// The interface is defined here in <c>Domain/</c> and implemented in <c>Providers/</c>, satisfying
/// <c>Rules.md</c> §5: infrastructure implements an abstraction defined by an inner layer.
/// </remarks>
public interface IPaymentProvider
{
    /// <summary>
    /// Stable key written to the intent row and used to select the provider:
    /// <c>"manual" | "mock" | "stripe"</c>.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// What this adapter can actually do. Callers must consult this rather than assume; a provider
    /// that cannot honour a call throws <see cref="PaymentProviderNotSupportedException"/>.
    /// </summary>
    PaymentProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Creates a charge the customer can complete. Idempotent on the request's idempotency key:
    /// repeating the same key returns the same provider intent instead of creating a second charge.
    /// </summary>
    Task<ProviderPaymentIntent> CreatePaymentIntentAsync(
        CreateProviderIntentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads current provider state. Returns null when the provider does not know the id.</summary>
    Task<ProviderPaymentIntent?> GetPaymentIntentAsync(
        string providerIntentId, CancellationToken cancellationToken = default);

    /// <summary>Voids an intent that has not settled.</summary>
    Task<ProviderPaymentIntent> CancelPaymentIntentAsync(
        string providerIntentId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Refunds a settled charge, wholly or partially, in minor units.</summary>
    Task<ProviderRefund> RefundAsync(
        ProviderRefundRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates a webhook and returns a provider-neutral event. Throws
    /// <see cref="PaymentWebhookVerificationException"/> when the signature or timestamp is invalid.
    /// </summary>
    PaymentWebhookEvent VerifyAndParseWebhook(PaymentWebhookRequest request);

    /// <summary>Creates or updates the provider's recurring agreement for a subscription.</summary>
    Task<ProviderSubscription> CreateOrUpdateSubscriptionAsync(
        CreateProviderSubscriptionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Schedules or performs cancellation of the provider's recurring agreement.</summary>
    Task<ProviderSubscription> CancelSubscriptionAsync(
        string providerSubscriptionId, bool atPeriodEnd, CancellationToken cancellationToken = default);
}
