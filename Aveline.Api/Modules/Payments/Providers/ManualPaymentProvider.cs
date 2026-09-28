using System.Collections.Concurrent;
using Aveline.Api.Modules.Payments.Domain;

namespace Aveline.Api.Modules.Payments.Providers;

/// <summary>
/// The manual adapter: the honest representation of what the system does today. A payment is
/// arranged out of band and an operator confirms receipt in the admin console; the adapter itself
/// has no hosted checkout, no refunds, no webhooks, and no recurring agreements.
/// </summary>
/// <remarks>
/// <para>
/// This adapter must never fabricate settlement. <see cref="CreatePaymentIntentAsync"/> returns
/// <see cref="PaymentProviderStatus.RequiresAction"/> with a null <c>CheckoutUrl</c> (it cannot
/// create a page to complete the charge on), and everything it cannot do throws
/// <see cref="PaymentProviderNotSupportedException"/> rather than returning a plausible object.
/// </para>
/// <para>
/// It holds no durable provider-side state: the provider intent id is derived deterministically
/// from Aveline's intent id, and within one scoped instance the adapter remembers the intents it
/// created so a replayed create returns the original rather than a second charge. That in-process
/// cache is the closest honest analogue of a provider's idempotency store; the durable dedup
/// identity is the intent row's filtered unique index, not this adapter.
/// </para>
/// </remarks>
internal sealed class ManualPaymentProvider : IPaymentProvider
{
    public const string ProviderKey = "manual";

    private readonly ConcurrentDictionary<string, ProviderPaymentIntent> _created =
        new(StringComparer.Ordinal);

    public string Key => ProviderKey;

    public PaymentProviderCapabilities Capabilities { get; } = new(
        SupportsRecurringSubscriptions: false,
        SupportsProration: false,
        SupportsPartialRefunds: false,
        SupportsCancelAtPeriodEnd: false,
        SupportsHostedCheckout: false,
        SettlesAsynchronously: true);

    public Task<ProviderPaymentIntent> CreatePaymentIntentAsync(
        CreateProviderIntentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Amount.AmountMinor <= 0)
        {
            throw new PaymentProviderNotSupportedException(
                "The manual provider cannot create a charge for a non-positive amount.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentProviderNotSupportedException(
                "The manual provider requires an idempotency key so a replay is not a second charge.");
        }

        // Idempotent on the request key: a replay returns the original intent, even when the retry
        // carries a different amount, exactly as a provider with an idempotency store would.
        var intent = _created.GetOrAdd(request.IdempotencyKey, _ => new ProviderPaymentIntent(
            ProviderIntentId: $"manual_{request.IntentId:N}",
            Status: PaymentProviderStatus.RequiresAction,
            Amount: request.Amount,
            CheckoutUrl: null,
            ClientSecret: null,
            ExpiresAt: request.ExpiresAt,
            FailureCode: null,
            FailureMessage: null));

        return Task.FromResult(intent);
    }

    /// <summary>
    /// Returns the intent this instance created, or null. The manual adapter has no provider-side
    /// record to read, so it reports null rather than inventing a settled status it cannot observe.
    /// </summary>
    public Task<ProviderPaymentIntent?> GetPaymentIntentAsync(
        string providerIntentId, CancellationToken cancellationToken = default)
    {
        var found = _created.Values.FirstOrDefault(
            intent => string.Equals(intent.ProviderIntentId, providerIntentId, StringComparison.Ordinal));

        return Task.FromResult(found);
    }

    public Task<ProviderPaymentIntent> CancelPaymentIntentAsync(
        string providerIntentId, string reason, CancellationToken cancellationToken = default) =>
        throw new PaymentProviderNotSupportedException(
            "The manual provider has no provider-side intent to cancel.");

    public Task<ProviderRefund> RefundAsync(
        ProviderRefundRequest request, CancellationToken cancellationToken = default) =>
        throw new PaymentProviderNotSupportedException(
            "The manual provider cannot refund: refunds are an operator action in the admin console.");

    public PaymentWebhookEvent VerifyAndParseWebhook(PaymentWebhookRequest request) =>
        throw new PaymentProviderNotSupportedException(
            "The manual provider receives no webhooks; there is no signature to verify.");

    public Task<ProviderSubscription> CreateOrUpdateSubscriptionAsync(
        CreateProviderSubscriptionRequest request, CancellationToken cancellationToken = default) =>
        throw new PaymentProviderNotSupportedException(
            "The manual provider does not support recurring provider subscriptions.");

    public Task<ProviderSubscription> CancelSubscriptionAsync(
        string providerSubscriptionId, bool atPeriodEnd, CancellationToken cancellationToken = default) =>
        throw new PaymentProviderNotSupportedException(
            "The manual provider does not support recurring provider subscriptions.");
}
