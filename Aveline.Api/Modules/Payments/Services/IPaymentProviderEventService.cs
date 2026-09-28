using Aveline.Api.Modules.Payments.Domain;

namespace Aveline.Api.Modules.Payments.Services;

/// <summary>
/// What ingesting one webhook delivery did. <see cref="IsNew"/> is false when the inbox already held
/// the provider event id, which the endpoint answers with 200: a provider retry is not an error.
/// </summary>
public sealed record WebhookOutcome(bool IsNew, bool Processed, Guid? InboxEventId, string? Detail);

/// <summary>
/// Verifies, dedupes and persists an inbound provider webhook, then dispatches settlement
/// (plan §6.2, §6.4 steps 6-7).
/// </summary>
public interface IPaymentProviderEventService
{
    /// <summary>
    /// Resolves the adapter by the stored provider key, verifies the signature and timestamp, stores
    /// the event once (the unique <c>(Provider, ProviderEventId)</c> index is the replay guard) and
    /// dispatches the settlement. Returns whether the event was new.
    /// </summary>
    /// <exception cref="PaymentWebhookVerificationException">
    /// The signature, secret or timestamp was invalid; the endpoint answers 403 with an empty body.
    /// </exception>
    Task<WebhookOutcome> IngestAsync(
        string providerKey, PaymentWebhookRequest request, CancellationToken cancellationToken = default);
}
