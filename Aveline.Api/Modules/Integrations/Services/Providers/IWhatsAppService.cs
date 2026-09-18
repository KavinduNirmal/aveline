namespace Aveline.Api.Modules.Integrations.Services.Providers;

/// <summary>Result of a WhatsApp connection test / token validation.</summary>
public sealed record WhatsAppTestResult(bool IsValid, string? Error = null);

/// <summary>Result of an outbound WhatsApp message send.</summary>
public sealed record WhatsAppSendResult(bool IsSuccess, string? MessageId = null, string? Error = null);

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

    /// <summary>Sends a plain-text WhatsApp message to a customer number.</summary>
    Task<WhatsAppSendResult> SendMessageAsync(
        string accessToken,
        string phoneNumberId,
        string to,
        string text,
        CancellationToken cancellationToken = default);
}
