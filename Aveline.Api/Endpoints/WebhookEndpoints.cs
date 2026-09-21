using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aveline.Api.Common.Media;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Infrastructure.RateLimiting;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Public webhook endpoints that Meta calls for inbound WhatsApp messages. These are NOT
/// JWT-protected (Meta has no Aveline token); instead each request is bound to an
/// organization via the route and authenticated by Meta's webhook signature / verify token.
///
/// <list type="bullet">
///   <item><c>GET /api/v1/webhooks/whatsapp/&#123;organizationId&#125;</c> — answers Meta's
///     subscription verification challenge.</item>
///   <item><c>POST /api/v1/webhooks/whatsapp/&#123;organizationId&#125;</c> — verifies the
///     <c>X-Hub-Signature-256</c> HMAC, persists an <see cref="InboundMessageLog"/>, and
///     publishes a <c>message.received</c> event on the Redis event bus for the agent service.</item>
/// </list>
/// </summary>
public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/webhooks/whatsapp/{organizationId:guid}");

        group.MapGet("", async (
            Guid organizationId,
            HttpRequest request,
            IIntegrationService integrationService,
            CancellationToken ct) =>
        {
            var mode = request.Query["hub.mode"].ToString();
            var verifyToken = request.Query["hub.verify_token"].ToString();
            var challenge = request.Query["hub.challenge"].ToString();

            if (!string.Equals(mode, "subscribe", StringComparison.Ordinal))
            {
                return Results.BadRequest(new { message = "Invalid hub.mode." });
            }

            var expected = await GetVerifyTokenAsync(organizationId, integrationService, ct);
            if (expected is null || !FixedTimeEquals(verifyToken, expected))
            {
                return Results.Forbid();
            }

            return Results.Text(challenge);
        }).AllowAnonymous();

        group.MapPost("", async (
            Guid organizationId,
            HttpContext httpContext,
            IIntegrationService integrationService,
            AppDbContext db,
            IEventBus eventBus,
            IRateLimiter rateLimiter,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            Aveline.Api.Modules.Conversations.Services.IConversationService conversations,
            Aveline.Api.Modules.Conversations.Services.IMessageBroadcaster broadcaster,
            Aveline.Api.Modules.CustomerConcierge.Repositories.ICustomerRepository customers,
            Aveline.Api.Modules.Integrations.Services.Providers.IWhatsAppService whatsApp,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Aveline.Webhooks.WhatsApp");

            // Guardrail 1: optional Meta IP allow-list. When configured, only requests from
            // those IPs are accepted (Meta publishes its webhook IP ranges).
            var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
            var allowedIps = configuration.GetSection("Webhook:AllowedIps").Get<string[]>();
            if (allowedIps is { Length: > 0 })
            {
                var allowed = allowedIps.Any(ip =>
                    string.Equals(ip, clientIp, StringComparison.OrdinalIgnoreCase));
                if (!allowed)
                {
                    logger.LogWarning(
                        "Rejected WhatsApp webhook from disallowed IP. organizationId={OrganizationId} ip={Ip}",
                        organizationId, clientIp);
                    return Results.Forbid();
                }
            }

            // Guardrail 2: verify the Meta signature BEFORE charging the per-org rate
            // limit. Otherwise an unauthenticated caller could exhaust a target's budget
            // with unsigned replays (M-4). Meta signs the raw body, so enable buffering
            // to read it once for the HMAC.
            httpContext.Request.EnableBuffering();
            byte[] body;
            using (var reader = new MemoryStream())
            {
                await httpContext.Request.Body.CopyToAsync(reader, ct);
                body = reader.ToArray();
            }

            var signature = httpContext.Request.Headers["X-Hub-Signature-256"].ToString();
            var appSecret = await GetAppSecretAsync(organizationId, integrationService, ct);
            if (appSecret is null || !WebhookSignatureVerifier.Verify(signature, body, appSecret))
            {
                logger.LogWarning(
                    "Rejected WhatsApp webhook with invalid signature. organizationId={OrganizationId}",
                    organizationId);
                return Results.Unauthorized();
            }

            // Guardrail 3: rate-limit verified inbound webhooks per organization + IP to
            // blunt replay/abuse. Only authentic traffic consumes the window.
            var rateKey = $"webhook:whatsapp:{organizationId}:{clientIp}";
            var allowedByRate = await rateLimiter.TryAllowAsync(
                rateKey, limit: 120, window: TimeSpan.FromMinutes(1), ct);
            if (!allowedByRate)
            {
                logger.LogWarning(
                    "WhatsApp webhook rate limit exceeded. organizationId={OrganizationId} ip={Ip}",
                    organizationId, clientIp);
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            // Parse the Meta payload.
            WhatsAppWebhookPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<WhatsAppWebhookPayload>(
                    body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                logger.LogWarning("Malformed WhatsApp webhook body. organizationId={OrganizationId}", organizationId);
                return Results.BadRequest(new { message = "Malformed payload." });
            }

            var message = ExtractMessage(payload);
            if (message is null)
            {
                // Not a message event (e.g. status/read receipt) — acknowledge without error.
                return Results.Ok(new { status = "ignored" });
            }

            // Persist a minimal audit record.
            try
            {
                db.InboundMessageLogs.Add(new InboundMessageLog
                {
                    OrganizationId = organizationId,
                    Channel = "whatsapp",
                    Direction = "inbound",
                    ExternalId = message.Id,
                    From = message.From,
                    To = message.To,
                    Content = message.Text,
                    ReceivedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                logger.LogInformation(
                    ex, "Duplicate inbound message acknowledged and dropped. externalId={ExternalId} orgId={OrgId}",
                    message.Id, organizationId);
                return Results.Ok(new { status = "duplicate_ignored" });
            }

            // Surface the inbound message in the Salon as a ClientMessage so staff see it
            // immediately (ADR-016). Best-effort: a failure here must not fail the webhook.
            //
            // A message with media but no caption is recorded too (D8): it used to be dropped
            // entirely, which is why a customer's photo produced `{status: "ignored"}`.
            Guid? attachmentId = null;
            if (!string.IsNullOrWhiteSpace(message.From)
                && (!string.IsNullOrWhiteSpace(message.Text) || message.Media is not null))
            {
                try
                {
                    // D1: the thread exists for the identified customer, so the phone is resolved
                    // synchronously at creation through the book rather than left to the agent.
                    // A number that is not on file yields a null customer, and the thread is
                    // created with only its external ref - the client renders that state.
                    var customer = await customers.GetByPhoneAsync(organizationId, message.From, ct);

                    // The customer's own media, stored before the message is recorded so the
                    // message can bind it. A missing integration or an expired media URL logs and
                    // records what it can rather than failing the webhook: Meta retries a non-200,
                    // and a retry would not fix an expired URL.
                    if (message.Media is not null)
                    {
                        attachmentId = await TryStoreInboundMediaAsync(
                            organizationId, message, customer?.Id, integrationService, whatsApp,
                            conversations, logger, ct);
                    }

                    var recorded = await conversations.RecordInboundClientMessageAsync(
                        organizationId, message.From, message.From, message.Text ?? string.Empty,
                        customer?.Id, attachmentId, ct);

                    // An inbound message may have created the thread, and `message.created` alone
                    // cannot deliver that: it carries a message for a conversation the client may
                    // never have seen. So the tile is broadcast from the creation site itself.
                    var tile = await conversations.GetTileAsync(recorded.ConversationId, ct);
                    if (tile is not null)
                    {
                        await broadcaster.BroadcastConversationChangedAsync(tile, ct);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to record inbound ClientMessage. organizationId={OrganizationId}",
                        organizationId);
                }
            }

            // Published after the record branch so it can name the attachment the thread now
            // carries: the agent's image analysis needs the id, not the bytes.
            await eventBus.PublishAsync(
                "message.received",
                organizationId,
                new
                {
                    messageId = message.Id,
                    from = message.From,
                    to = message.To,
                    text = message.Text,
                    attachmentId,
                    timestamp = DateTimeOffset.UtcNow,
                },
                traceId: null,
                ct);

            logger.LogInformation(
                "Processed inbound WhatsApp message. organizationId={OrganizationId} messageId={MessageId}",
                organizationId, message.Id);

            return Results.Ok(new { status = "received" });
        }).AllowAnonymous();

        return endpoints;
    }

    /// <summary>
    /// Constant-time string comparison for secret material (the GET verify token), so a
    /// caller cannot recover the token byte by byte through response timing (M-4).
    /// </summary>
    private static bool FixedTimeEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
    }

    private static async Task<string?> GetVerifyTokenAsync(
        Guid organizationId,
        IIntegrationService integrationService,
        CancellationToken ct)
    {
        try
        {
            var creds = await integrationService.GetCredentialsAsync(organizationId, IntegrationType.WhatsApp, ct);
            creds.TryGetValue("webhookVerifyToken", out var token);
            return token;
        }
        catch (IntegrationNotConfiguredException)
        {
            return null;
        }
    }

    /// <summary>
    /// Fetches the customer's media with the tenant's credentials and stores it against the
    /// thread, returning the attachment id or <c>null</c> when it could not be kept.
    /// </summary>
    /// <remarks>
    /// Every failure path here is a log-and-continue: a missing integration, an expired media
    /// URL, a download error or a type outside the allow-list must never make the webhook
    /// answer non-200, because Meta's retry would not fix any of them.
    /// </remarks>
    private static async Task<Guid?> TryStoreInboundMediaAsync(
        Guid organizationId,
        ExtractedMessage message,
        Guid? customerId,
        IIntegrationService integrationService,
        Aveline.Api.Modules.Integrations.Services.Providers.IWhatsAppService whatsApp,
        Aveline.Api.Modules.Conversations.Services.IConversationService conversations,
        ILogger logger,
        CancellationToken ct)
    {
        var media = message.Media!;
        if (string.IsNullOrWhiteSpace(media.Id) || string.IsNullOrWhiteSpace(message.From))
        {
            return null;
        }

        try
        {
            var credentials = await integrationService.GetCredentialsAsync(
                organizationId, IntegrationType.WhatsApp, ct);
            if (!credentials.TryGetValue("accessToken", out var accessToken)
                || string.IsNullOrWhiteSpace(accessToken))
            {
                logger.LogWarning(
                    "Inbound WhatsApp media skipped: no access token. organizationId={OrganizationId}",
                    organizationId);
                return null;
            }

            var fetched = await whatsApp.GetMediaAsync(accessToken, media.Id, ct);
            if (!fetched.IsSuccess || fetched.Bytes is null || fetched.Bytes.Length == 0)
            {
                logger.LogWarning(
                    "Inbound WhatsApp media could not be fetched. organizationId={OrganizationId} error={Error}",
                    organizationId, fetched.Error);
                return null;
            }

            var fileName = InboundMediaFileName(message.Id, fetched.ContentType ?? media.MimeType);
            var contentType = AttachmentContentPolicy.ResolveForStorage(
                fetched.ContentType ?? media.MimeType, fileName, fetched.Bytes);
            if (contentType is null)
            {
                // Audio, video, anything else off the allow-list, or an image claim the bytes do
                // not support: recorded as skipped rather than stored.
                logger.LogInformation(
                    "Inbound WhatsApp media skipped: unsupported or mislabelled type. organizationId={OrganizationId} type={Type}",
                    organizationId, fetched.ContentType ?? media.MimeType);
                return null;
            }

            var stored = await conversations.StoreInboundAttachmentAsync(
                organizationId, message.From, customerId, fetched.Bytes, contentType, fileName, ct);
            return stored.Id;
        }
        catch (IntegrationNotConfiguredException)
        {
            logger.LogWarning(
                "Inbound WhatsApp media skipped: the integration is not configured. organizationId={OrganizationId}",
                organizationId);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Inbound WhatsApp media failed. organizationId={OrganizationId}", organizationId);
            return null;
        }
    }

    /// <summary>A file name for media Meta sends without one.</summary>
    private static string InboundMediaFileName(string? externalId, string? contentType)
    {
        var extension = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/avif" => ".avif",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            "image/heic" => ".heic",
            "image/heif" => ".heif",
            _ => ".jpg",
        };
        return $"whatsapp-{externalId ?? "media"}{extension}";
    }

    private static async Task<string?> GetAppSecretAsync(
        Guid organizationId,
        IIntegrationService integrationService,
        CancellationToken ct)
    {
        try
        {
            var creds = await integrationService.GetCredentialsAsync(organizationId, IntegrationType.WhatsApp, ct);
            creds.TryGetValue("appSecret", out var secret);
            return secret;
        }
        catch (IntegrationNotConfiguredException)
        {
            return null;
        }
    }

    /// <summary>
    /// The message Meta sent, whether it is text or media.
    /// </summary>
    /// <remarks>
    /// This used to select only `type == "text"`, so an image or a document produced
    /// `{status: "ignored"}` and nothing in the thread. Media now yields a descriptor plus any
    /// caption, and the caption is the message's own words when it has them.
    /// </remarks>
    private static ExtractedMessage? ExtractMessage(WhatsAppWebhookPayload? payload)
    {
        var entry = payload?.Entry?.FirstOrDefault();
        var change = entry?.Changes?.FirstOrDefault();
        var value = change?.Value;
        var message = value?.Messages?.FirstOrDefault();
        if (message is null)
        {
            return null;
        }

        var media = message.Image ?? message.Document;

        return new ExtractedMessage
        {
            Id = message.Id,
            From = message.From,
            To = value!.Contacts?.FirstOrDefault()?.WaId ?? value.Metadata?.DisplayPhoneNumber,
            Text = message.Text?.Body ?? media?.Caption,
            Media = media,
        };
    }

    private sealed record WhatsAppWebhookPayload(WhatsAppEntry[]? Entry);
    private sealed record WhatsAppEntry(WhatsAppChange[]? Changes);
    private sealed record WhatsAppChange(WhatsAppValue? Value);
    private sealed record WhatsAppValue(
        WhatsAppMessage[]? Messages,
        WhatsAppContact[]? Contacts,
        WhatsAppMetadata? Metadata);
    private sealed record WhatsAppMessage(
        string? Id,
        string? From,
        string? Type,
        WhatsAppText? Text,
        WhatsAppMedia? Image,
        WhatsAppMedia? Document);

    private sealed record WhatsAppText(string? Body);

    /// <summary>Meta's media object: `{ id, mime_type, sha256, caption? }`.</summary>
    private sealed record WhatsAppMedia(
        string? Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("mime_type")] string? MimeType,
        string? Sha256,
        string? Caption);
    private sealed record WhatsAppContact(string? WaId);
    private sealed record WhatsAppMetadata(string? DisplayPhoneNumber);

    private sealed class ExtractedMessage
    {
        public string? Id { get; init; }
        public string? From { get; init; }
        public string? To { get; init; }
        public string? Text { get; init; }

        /// <summary>The image or document the customer sent, when there was one.</summary>
        public WhatsAppMedia? Media { get; init; }
    }
}
