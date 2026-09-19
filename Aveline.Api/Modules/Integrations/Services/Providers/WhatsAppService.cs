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
    public async Task<WhatsAppMediaResult> GetMediaAsync(
        string accessToken,
        string mediaId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);

        try
        {
            // Step 1: the media id resolves to a short-lived URL on Meta's CDN.
            using var resolve = new HttpRequestMessage(HttpMethod.Get, mediaId);
            resolve.Headers.Authorization = new("Bearer", accessToken);
            using var resolved = await _http.SendAsync(resolve, cancellationToken);
            if (!resolved.IsSuccessStatusCode)
            {
                var resolveError = await ReadErrorAsync(resolved, cancellationToken);
                _logger.LogWarning(
                    "WhatsApp media resolve failed. mediaId={MediaId} status={Status} error={Error}",
                    mediaId, (int)resolved.StatusCode, resolveError);
                return new WhatsAppMediaResult(IsSuccess: false, Error: resolveError);
            }

            var metadata = await resolved.Content.ReadFromJsonAsync<MediaMetadata>(cancellationToken);
            if (string.IsNullOrWhiteSpace(metadata?.Url))
            {
                _logger.LogWarning("WhatsApp media resolve returned no URL. mediaId={MediaId}", mediaId);
                return new WhatsAppMediaResult(IsSuccess: false, Error: "Meta returned no media URL.");
            }

            // Step 2: the URL still needs the bearer; it is not public.
            using var download = new HttpRequestMessage(HttpMethod.Get, metadata.Url);
            download.Headers.Authorization = new("Bearer", accessToken);
            using var media = await _http.SendAsync(download, cancellationToken);
            if (!media.IsSuccessStatusCode)
            {
                var downloadError = await ReadErrorAsync(media, cancellationToken);
                _logger.LogWarning(
                    "WhatsApp media download failed. mediaId={MediaId} status={Status} error={Error}",
                    mediaId, (int)media.StatusCode, downloadError);
                return new WhatsAppMediaResult(IsSuccess: false, Error: downloadError);
            }

            var bytes = await media.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = media.Content.Headers.ContentType?.MediaType
                              ?? metadata.MimeType
                              ?? "application/octet-stream";
            _logger.LogInformation(
                "WhatsApp media fetched. mediaId={MediaId} bytes={Bytes} contentType={ContentType}",
                mediaId, bytes.Length, contentType);

            return new WhatsAppMediaResult(
                IsSuccess: true,
                Bytes: bytes,
                ContentType: contentType,
                SizeBytes: bytes.LongLength);
        }
        catch (Exception ex)
        {
            // A media fetch must never take the webhook down: the caller records what it can.
            _logger.LogError(ex, "WhatsApp media fetch threw. mediaId={MediaId}", mediaId);
            return new WhatsAppMediaResult(IsSuccess: false, Error: ex.Message);
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

    /// <summary>Meta's media metadata; the JSON is snake_case.</summary>
    private sealed record MediaMetadata(
        [property: System.Text.Json.Serialization.JsonPropertyName("url")] string? Url,
        [property: System.Text.Json.Serialization.JsonPropertyName("mime_type")] string? MimeType,
        [property: System.Text.Json.Serialization.JsonPropertyName("file_size")] long? FileSize);

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
