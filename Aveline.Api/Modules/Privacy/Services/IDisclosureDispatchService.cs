namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// The result of one disclosure dispatch attempt (plan §4.4). The vocabulary is bounded so the
/// caller can decide whether the next inbound message should retry without parsing an error string.
/// </summary>
public enum DisclosureDispatchOutcome
{
    /// <summary>The provider accepted the disclosure.</summary>
    Sent,

    /// <summary>The consent row was already stamped, so nothing was sent. The common case.</summary>
    AlreadyDisclosed,

    /// <summary>The boutique's WhatsApp integration is absent or half-configured; nothing was sent.</summary>
    NotConfigured,

    /// <summary>The provider refused the message; the claim was released for a retry.</summary>
    Failed,

    /// <summary>No signing key is configured, so no opt-out link could be built; nothing was sent.</summary>
    SigningKeyMissing,
}

/// <summary>One dispatch outcome, with the provider's message id when it sent.</summary>
public sealed record DisclosureDispatchResult(
    DisclosureDispatchOutcome Outcome,
    string? ProviderMessageId = null,
    string? Error = null);

/// <summary>
/// Sends the first-contact disclosure exactly once (plan §4.4). It is the only writer of
/// <c>CustomerConsent.DisclosureShownAt</c> for the disclosure flow, and the conditional update it
/// performs is what makes "exactly once" true under concurrent inbound messages.
/// </summary>
public interface IDisclosureDispatchService
{
    /// <summary>
    /// Runs the §4.4 sequence for one inbound message. Never throws for a provider,
    /// configuration or signing failure: those are returned as outcomes, because the inbound
    /// webhook's contract is to record what it can and answer 200.
    /// </summary>
    /// <param name="organizationId">The boutique the message arrived for.</param>
    /// <param name="customerId">The identified customer whose consent row carries the stamp.</param>
    /// <param name="toE164">The number the customer wrote from, in E.164 form.</param>
    Task<DisclosureDispatchResult> DispatchAsync(
        Guid organizationId,
        Guid customerId,
        string toE164,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The stable idempotency key for one customer's disclosure at one version. Stable across
    /// retries and instances so the outbound channel's pre-check replays instead of re-sending.
    /// </summary>
    static string BuildIdempotencyKey(Guid organizationId, Guid customerId, string disclosureVersion)
        => $"disclosure:{organizationId:D}:{customerId:D}:{disclosureVersion}";
}
