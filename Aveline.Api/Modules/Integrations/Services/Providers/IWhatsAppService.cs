namespace Aveline.Api.Modules.Integrations.Services.Providers;

/// <summary>Result of a WhatsApp connection test / token validation.</summary>
public sealed record WhatsAppTestResult(bool IsValid, string? Error = null);

/// <summary>Result of an outbound WhatsApp message send.</summary>
/// <param name="IsSuccess">True only when Meta accepted the message.</param>
/// <param name="MessageId">Meta's <c>wamid</c> for the accepted message.</param>
/// <param name="Error">
/// Meta's <c>error.message</c>, or the transport exception's message. Only ever shaped for a log
/// line: it can echo the request, so it is never surfaced to a customer.
/// </param>
/// <param name="HttpStatus">
/// The HTTP status Meta answered with, or <c>null</c> when the call failed before a response
/// existed (DNS, TLS, timeout, socket reset). This is what lets the outbound retry layer decide
/// "retry 429/5xx and transport, do not retry any other 4xx" from data rather than by parsing
/// <paramref name="Error"/> (privacy plan §6.2).
/// </param>
public sealed record WhatsAppSendResult(
    bool IsSuccess,
    string? MessageId = null,
    string? Error = null,
    int? HttpStatus = null);

/// <summary>Result of a media fetch from Meta.</summary>
public sealed record WhatsAppMediaResult(
    bool IsSuccess,
    byte[]? Bytes = null,
    string? ContentType = null,
    long? SizeBytes = null,
    string? Error = null);

/// <summary>
/// Outbound provider for the Meta WhatsApp Cloud API. All calls are made from the backend
/// (never from clients) using the tenant's own credentials. Secrets and message bodies are
/// masked in logs. The concrete implementation is a typed <see cref="HttpClient"/>.
/// </summary>
public interface IWhatsAppService
{
    /// <summary>
    /// Validates that the access token can reach the given phone number (a zero-cost
    /// <c>GET /&#123;phone-number-id&#125;</c>). Used by "Test &amp; Connect" and the health service.
    /// </summary>
    Task<WhatsAppTestResult> TestConnectionAsync(
        string accessToken,
        string phoneNumberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an inbound media item (a customer's photo or document) using Meta's two-step
    /// fetch: resolve the media id to a short-lived URL, then download that URL with the same
    /// bearer. Media URLs expire, so the bytes are read immediately rather than stored.
    /// </summary>
    Task<WhatsAppMediaResult> GetMediaAsync(
        string accessToken,
        string mediaId,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a plain-text WhatsApp message to a customer number.</summary>
    Task<WhatsAppSendResult> SendMessageAsync(
        string accessToken,
        string phoneNumberId,
        string to,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an approved message template. This is the only mechanism Meta offers for a
    /// business-initiated message outside an open customer session, so it is the prerequisite for
    /// any proactive (non-reply) customer messaging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Policy gate (privacy plan open question Q-2).</b> Whether Meta requires a template for a
    /// business-initiated message <i>inside</i> an open session could not be confirmed against
    /// Meta's own documentation. The provider method and the outbound channel path exist so that
    /// the capability is implementable and testable, but <b>no proactive (non-reply) messaging may
    /// be wired to it until policy confirms the template rules.</b> Reply-path free-form text
    /// inside the 24-hour window is what Phase 3's disclosure uses.
    /// </para>
    /// <para>
    /// <paramref name="components"/> follows Meta's own shape (an array of
    /// <c>{ type, parameters[] }</c> objects) and is passed through unchanged; an empty or null
    /// value omits the key entirely, because Meta rejects an empty <c>components</c> array.
    /// </para>
    /// </remarks>
    Task<WhatsAppSendResult> SendTemplateAsync(
        string accessToken,
        string phoneNumberId,
        string to,
        string templateName,
        string languageCode,
        IReadOnlyList<object>? components,
        CancellationToken cancellationToken = default);
}
