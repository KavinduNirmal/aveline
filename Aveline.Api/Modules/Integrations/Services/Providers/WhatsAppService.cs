using System.Net.Http.Json;
using System.Text.Json;
using Aveline.Api.Common.Media;
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
            // Step 1: the media id resolves to a short-lived URL on Meta's CDN. Headers only:
            // the body of neither hop should be buffered by `HttpClient`, or the cap below
            // would be applied after the whole object was already in memory.
            using var resolve = new HttpRequestMessage(HttpMethod.Get, mediaId);
            resolve.Headers.Authorization = new("Bearer", accessToken);
            using var resolved = await _http.SendAsync(
                resolve, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

            // Step 2: the URL still needs the bearer; it is not public. `ResponseHeadersRead`
            // keeps the body unbuffered, which is what makes the capped streaming read below a
            // real bound rather than a check applied to an already-materialised array.
            using var download = new HttpRequestMessage(HttpMethod.Get, metadata.Url);
            download.Headers.Authorization = new("Bearer", accessToken);
            using var media = await _http.SendAsync(
                download, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!media.IsSuccessStatusCode)
            {
                var downloadError = await ReadErrorAsync(media, cancellationToken);
                _logger.LogWarning(
                    "WhatsApp media download failed. mediaId={MediaId} status={Status} error={Error}",
                    mediaId, (int)media.StatusCode, downloadError);
                return new WhatsAppMediaResult(IsSuccess: false, Error: downloadError);
            }

            var contentType = media.Content.Headers.ContentType?.MediaType
                              ?? metadata.MimeType
                              ?? "application/octet-stream";

            // The cap is the attachment tier's own bound, so the fetch and the store agree on
            // "too big" (strategy §5.1 S4; salon §7.4). A declared over-cap length is refused
            // before a byte is read; the header is the sender's, though, so the body is then
            // streamed through a bounded reader rather than trusted to it.
            if (media.Content.Headers.ContentLength is { } declaredLength
                && declaredLength > MediaContentTypes.MaxFileBytes)
            {
                _logger.LogWarning(
                    "WhatsApp media refused: the declared size exceeds the cap. mediaId={MediaId} declared={DeclaredBytes} cap={CapBytes}",
                    mediaId, declaredLength, MediaContentTypes.MaxFileBytes);
                return new WhatsAppMediaResult(IsSuccess: false, ContentType: contentType, Error: OverCapError);
            }

            var bytes = await ReadCappedAsync(
                media.Content, MediaContentTypes.MaxFileBytes, cancellationToken);
            if (bytes is null)
            {
                // A clean refusal, never a truncation: half an image stored silently is worse
                // than a recorded skip.
                _logger.LogWarning(
                    "WhatsApp media refused: the body exceeds the cap. mediaId={MediaId} cap={CapBytes}",
                    mediaId, MediaContentTypes.MaxFileBytes);
                return new WhatsAppMediaResult(IsSuccess: false, ContentType: contentType, Error: OverCapError);
            }

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

    /// <summary>The refusal text, so the cap is named rather than implied.</summary>
    private static string OverCapError =>
        $"The media object exceeds the {MediaContentTypes.MaxFileBytes}-byte attachment limit.";

    /// <summary>
    /// Reads a response body into memory without ever holding more than
    /// <paramref name="cap"/> bytes plus one buffer, and returns <c>null</c> the moment the cap
    /// is crossed. A response that declares no length is bounded here too, which is the whole
    /// point: <c>ReadAsByteArrayAsync</c> had no bound at all.
    /// </summary>
    private static async Task<byte[]?> ReadCappedAsync(
        HttpContent content, long cap, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];

        while (true)
        {
            var read = await source.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > cap)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
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
