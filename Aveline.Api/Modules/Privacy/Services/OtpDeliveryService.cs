using Aveline.Api.Modules.Integrations.Services;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// Sends the opt-out OTP (plan §5.2 step 2). It is separated from <see cref="IOtpService"/> so the
/// code's lifecycle - minted, hashed, verified, consumed - is testable without a provider, and so
/// the delivery path is the same <see cref="IOutboundMessagingService"/> the disclosure and the
/// acknowledgement use.
/// </summary>
public interface IOtpDeliveryService
{
    /// <summary>
    /// Delivers <paramref name="code"/> to the customer. Returns the channel's outcome; never throws
    /// for a provider or configuration failure.
    /// </summary>
    Task<OutboundMessageResult> SendAsync(
        Guid organizationId,
        string phoneE164,
        string handle,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the boutique can receive an OTP at all today. The start endpoint uses this to decide
    /// whether to mint a code; a boutique with no WhatsApp connection must not burn the customer's
    /// send budget on a message that cannot be delivered.
    /// </summary>
    Task<bool> IsConfiguredAsync(
        Guid organizationId, CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="IOtpDeliveryService"/> over the WhatsApp outbound channel.</summary>
public sealed class OtpDeliveryService : IOtpDeliveryService
{
    /// <summary>The channel the OTP is sent on. The only one wired today (DR-5).</summary>
    public const string ChannelKey = "whatsapp";

    private readonly IOutboundMessagingService _outbound;

    public OtpDeliveryService(IOutboundMessagingService outbound) => _outbound = outbound;

    /// <inheritdoc />
    public async Task<OutboundMessageResult> SendAsync(
        Guid organizationId,
        string phoneE164,
        string handle,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneE164);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return await _outbound.SendWhatsAppTextAsync(
            organizationId,
            phoneE164,
            BuildBody(code),
            BuildIdempotencyKey(organizationId, handle),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsConfiguredAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
        => _outbound.IsChannelConfiguredAsync(organizationId, ChannelKey, cancellationToken);

    /// <summary>
    /// The OTP message. No link, no boutique name, no personal detail: the code and its lifetime.
    /// Kept here rather than in a template so the Q-2 template gate is not involved (this is a
    /// reply-path message in response to the customer's own request).
    /// </summary>
    internal static string BuildBody(string code)
        => $"Your Aveline opt-out code is {code}. It expires in 5 minutes. "
           + "If you did not request this, ignore this message.";

    /// <summary>
    /// The stable idempotency key. It carries the opaque handle, never the phone, so a replayed start
    /// cannot produce a second Meta call and the key discloses nothing on its own.
    /// </summary>
    internal static string BuildIdempotencyKey(Guid organizationId, string handle)
        => $"otp:{organizationId:D}:{handle}";
}
