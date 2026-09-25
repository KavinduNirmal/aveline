namespace Aveline.Api.Modules.Integrations.Services;

/// <summary>
/// The single entry point for business-initiated outbound messaging (privacy plan §6.2). It exists
/// because the welcome disclosure, the OTP delivery and the opt-out acknowledgement all need the
/// same four properties — per-org credentials, a stable idempotency key, bounded retry, and an
/// outbound audit row — and none of them should re-implement them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not <c>ICustomerDeliveryService</c>.</b> The staff console's reply path is a
/// different contract: it records a <c>Message</c> in a conversation and reports refusals to an
/// associate. This path records an <c>InboundMessageLog</c> with <c>Direction = "outbound"</c> and
/// returns a machine-readable result so a retrying privacy flow can tell "already sent" from
/// "provider refused" from "not configured". Sharing the provider is deliberate; sharing the
/// record shape is not.
/// </para>
/// <para>
/// <b>Never logs the message body.</b> Both this service's channels and the provider log only
/// identifiers and masked numbers.
/// </para>
/// </remarks>
public interface IOutboundMessagingService
{
    /// <summary>
    /// Sends a reply-path WhatsApp text on behalf of the boutique, using that organization's own
    /// credentials. Never throws for a provider or configuration failure: those are returned as
    /// <c>IsSuccess = false</c> so the caller can decide whether the surrounding operation should
    /// fail.
    /// </summary>
    /// <param name="organizationId">The boutique whose credentials are used.</param>
    /// <param name="toE164">The customer's number in E.164 form.</param>
    /// <param name="text">The body. Recorded on success, never logged.</param>
    /// <param name="idempotencyKey">
    /// Caller-supplied and stable across retries. The same key never produces a second provider
    /// call.
    /// </param>
    Task<OutboundMessageResult> SendWhatsAppTextAsync(
        Guid organizationId,
        string toE164,
        string text,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an approved WhatsApp template. <b>Gated on policy question Q-2</b>: no proactive
    /// (non-reply) customer messaging may be wired to this until Meta's template rules are
    /// confirmed. The path exists so the capability is not blocked behind a rewrite.
    /// </summary>
    Task<OutboundMessageResult> SendWhatsAppTemplateAsync(
        Guid organizationId,
        string toE164,
        string templateName,
        string languageCode,
        IReadOnlyList<object>? components,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the organization can actually send on the given channel key today. False when the
    /// channel is absent <i>and</i> when its provider does not exist (Instagram).
    /// </summary>
    Task<bool> IsChannelConfiguredAsync(
        Guid organizationId,
        string channelKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the channel a send should go out on. Kept separate from the service so a caller that
/// already knows the channel (the disclosure flow always uses WhatsApp today) can address it
/// directly, and so the set of channels is inspectable without reflecting over DI.
/// </summary>
public interface IOutboundChannelRegistry
{
    /// <summary>The channels available in this deployment, keyed by <see cref="IOutboundChannel.ChannelKey"/>.</summary>
    IReadOnlyDictionary<string, IOutboundChannel> Channels { get; }

    /// <summary>Resolves a channel by key, or <c>null</c> when the key is unknown.</summary>
    IOutboundChannel? Resolve(string channelKey);
}
