namespace Aveline.Api.Modules.Integrations.Services;

/// <summary>
/// The outcome of one outbound message attempt. A provider failure and a configuration failure are
/// both returned, never thrown: the caller decides whether the surrounding operation should fail,
/// and a privacy flow must not take the inbound webhook down because a boutique has not connected
/// WhatsApp yet.
/// </summary>
/// <param name="IsSuccess">True only when the channel's provider accepted the message.</param>
/// <param name="ProviderMessageId">
/// The channel's own message id (Meta's <c>wamid</c>), when it returned one.
/// </param>
/// <param name="Error">
/// A non-secret diagnostic. It can echo the request, so it is for logs and staff-facing messages
/// only and is never shown to a customer verbatim.
/// </param>
/// <param name="Skipped">
/// True when nothing was attempted because the channel is not configured for this organization.
/// Separates "we did not try" from "we tried and failed", which is the difference between a
/// metric that means a missing credential and one that means a provider outage.
/// </param>
public sealed record OutboundMessageResult(
    bool IsSuccess,
    string? ProviderMessageId = null,
    string? Error = null,
    bool Skipped = false)
{
    /// <summary>The provider accepted the message.</summary>
    public static OutboundMessageResult Sent(string? providerMessageId) =>
        new(IsSuccess: true, ProviderMessageId: providerMessageId);

    /// <summary>The channel is not configured for the organization; nothing was sent.</summary>
    public static OutboundMessageResult NotConfigured(string error) =>
        new(IsSuccess: false, Error: error, Skipped: true);

    /// <summary>An attempt was made and the provider refused it.</summary>
    public static OutboundMessageResult Failed(string error) =>
        new(IsSuccess: false, Error: error);
}

/// <summary>
/// The bounded vocabulary of <c>OutboundMessageResult</c> outcomes, used as the metric label
/// (plan §9.1, <c>outbound_message_result_total{channel,result}</c>). Constants rather than free
/// text: a counter whose label values drift is useless to alert on.
/// </summary>
public static class OutboundOutcomes
{
    /// <summary>A provider accepted the message.</summary>
    public const string Sent = "sent";

    /// <summary>The channel is not configured; no provider call was made.</summary>
    public const string Skipped = "skipped";

    /// <summary>The provider refused the message, or every retry failed.</summary>
    public const string Failed = "failed";
}

/// <summary>
/// One delivery channel, addressed by key. The abstraction exists so the disclosure, OTP and
/// opt-out flows are written once and a second channel (Instagram) is a new class rather than a
/// new flow (plan §6.3).
/// </summary>
public interface IOutboundChannel
{
    /// <summary>The channel's stable key, e.g. <c>whatsapp</c>. Also the log/metric label value.</summary>
    string ChannelKey { get; }

    /// <summary>
    /// Sends free-form text on behalf of the organization. Must never throw for a provider or
    /// configuration failure: those are returned as a failed or skipped
    /// <see cref="OutboundMessageResult"/>.
    /// </summary>
    /// <param name="organizationId">The boutique whose own provider credentials are used.</param>
    /// <param name="toE164">The customer's number in E.164 form.</param>
    /// <param name="text">The message body. Never logged.</param>
    /// <param name="idempotencyKey">
    /// Stable across retries of the same logical message. It is both the pre-check key and the
    /// <c>ExternalId</c> recorded on success, so an at-least-once caller cannot double-send.
    /// </param>
    Task<OutboundMessageResult> SendTextAsync(
        Guid organizationId,
        string toE164,
        string text,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an approved template. <b>Gated on policy question Q-2:</b> the capability exists so
    /// proactive messaging is implementable, but no proactive (non-reply) sender may be wired to
    /// it until Meta's template rules are confirmed.
    /// </summary>
    Task<OutboundMessageResult> SendTemplateAsync(
        Guid organizationId,
        string toE164,
        string templateName,
        string languageCode,
        IReadOnlyList<object>? components,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this organization can actually send on this channel. False for a channel whose
    /// credentials exist but whose provider does not (Instagram today), so a parity badge can
    /// stop reporting "connected" for something Aveline cannot use.
    /// </summary>
    Task<bool> IsConfiguredAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
