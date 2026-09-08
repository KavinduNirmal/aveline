using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Integrations.Services.Providers;

/// <summary>
/// Meta WhatsApp Cloud API provider backed by a typed <see cref="HttpClient"/>.
/// Base URL and API version come from configuration (<c>WhatsApp:BaseUrl</c>,
/// <c>WhatsApp:ApiVersion</c>). Secrets and message bodies are never logged.
/// </summary>
public sealed class WhatsAppService : IWhatsAppService
{
    private readonly HttpClient _http;
    private readonly ILogger<WhatsAppService> _logger;

    public WhatsAppService(HttpClient http, ILogger<WhatsAppService> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<WhatsAppTestResult> TestConnectionAsync(
        string accessToken,
        string phoneNumberId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumberId);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{phoneNumberId}");
        request.Headers.Authorization = new("Bearer", accessToken);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "WhatsApp connection test succeeded. phoneNumberId={PhoneNumberId}", phoneNumberId);
                return new WhatsAppTestResult(IsValid: true);
            }

            var error = await ReadErrorAsync(response, cancellationToken);
            _logger.LogWarning(
                "WhatsApp connection test failed. phoneNumberId={PhoneNumberId} status={Status} error={Error}",
                phoneNumberId, (int)response.StatusCode, error);
            return new WhatsAppTestResult(IsValid: false, Error: error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp connection test threw. phoneNumberId={PhoneNumberId}", phoneNumberId);
            return new WhatsAppTestResult(IsValid: false, Error: ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<WhatsAppSendResult> SendMessageAsync(
        string accessToken,
        string phoneNumberId,
        string to,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumberId);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to,
            type = "text",
            text = new { preview_url = false, body = text },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{phoneNumberId}/messages")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new("Bearer", accessToken);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var messageId = ExtractMessageId(body);
                _logger.LogInformation(
                    "WhatsApp message sent. phoneNumberId={PhoneNumberId} to={To} messageId={MessageId}",
                    phoneNumberId, Mask(to), messageId);
                return new WhatsAppSendResult(IsSuccess: true, MessageId: messageId);
            }

            var error = await ReadErrorAsync(response, cancellationToken);
            _logger.LogWarning(
                "WhatsApp message send failed. phoneNumberId={PhoneNumberId} to={To} status={Status} error={Error}",
                phoneNumberId, Mask(to), (int)response.StatusCode, error);
            return new WhatsAppSendResult(IsSuccess: false, Error: error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp message send threw. phoneNumberId={PhoneNumberId} to={To}",
                phoneNumberId, Mask(to));
            return new WhatsAppSendResult(IsSuccess: false, Error: ex.Message);
        }
    }

    private static string? ExtractMessageId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("messages", out var messages)
                   && messages.ValueKind == JsonValueKind.Array
                   && messages.GetArrayLength() > 0
                   && messages[0].TryGetProperty("id", out var id)
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? $"HTTP {(int)response.StatusCode}";
            }
        }
        catch (JsonException)
        {
            // fall through to the status-code fallback
        }

        return $"HTTP {(int)response.StatusCode}";
    }

    /// <summary>Masks a phone number for safe logging, e.g. <c>+94****1234</c>.</summary>
    private static string Mask(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "****";
        }

        return value.Length <= 8 ? "****" : value[..3] + "****" + value[^4..];
    }
}
