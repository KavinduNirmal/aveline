using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Infrastructure.RateLimiting;
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
            if (expected is null || !string.Equals(verifyToken, expected, StringComparison.Ordinal))
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

            // Guardrail 2: rate-limit inbound webhooks per organization + IP to blunt replay/abuse.
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

            // Meta signs the raw body; enable buffering so we can read it once for HMAC.
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

            // Publish to the agent service for processing (fire-and-forget).
            await eventBus.PublishAsync(
                "message.received",
                organizationId,
                new
                {
                    messageId = message.Id,
                    from = message.From,
                    to = message.To,
                    text = message.Text,
                    timestamp = DateTimeOffset.UtcNow,
                },
                traceId: null,
                ct);

            // Surface the inbound message in the Salon as a ClientMessage so staff see it
            // immediately (ADR-016). Best-effort: a failure here must not fail the webhook.
            if (!string.IsNullOrWhiteSpace(message.From) && !string.IsNullOrWhiteSpace(message.Text))
            {
                try
                {
                    await conversations.RecordInboundClientMessageAsync(
                        organizationId, message.From, message.From, message.Text, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to record inbound ClientMessage. organizationId={OrganizationId}",
                        organizationId);
                }
            }

            logger.LogInformation(
                "Processed inbound WhatsApp message. organizationId={OrganizationId} messageId={MessageId}",
                organizationId, message.Id);

            return Results.Ok(new { status = "received" });
        }).AllowAnonymous();

        return endpoints;
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

    private static ExtractedMessage? ExtractMessage(WhatsAppWebhookPayload? payload)
    {
        var entry = payload?.Entry?.FirstOrDefault();
        var change = entry?.Changes?.FirstOrDefault();
        var value = change?.Value;
        var message = value?.Messages?.FirstOrDefault(m => m.Type == "text");
        if (message is null)
        {
            return null;
        }

        return new ExtractedMessage
        {
            Id = message.Id,
            From = message.From,
            To = value.Contacts?.FirstOrDefault()?.WaId ?? value.Metadata?.DisplayPhoneNumber,
            Text = message.Text?.Body,
        };
    }

    private sealed record WhatsAppWebhookPayload(WhatsAppEntry[]? Entry);
    private sealed record WhatsAppEntry(WhatsAppChange[]? Changes);
    private sealed record WhatsAppChange(WhatsAppValue? Value);
    private sealed record WhatsAppValue(
        WhatsAppMessage[]? Messages,
        WhatsAppContact[]? Contacts,
        WhatsAppMetadata? Metadata);
    private sealed record WhatsAppMessage(string? Id, string? From, string? Type, WhatsAppText? Text);
    private sealed record WhatsAppText(string? Body);
    private sealed record WhatsAppContact(string? WaId);
    private sealed record WhatsAppMetadata(string? DisplayPhoneNumber);

    private sealed class ExtractedMessage
    {
        public string? Id { get; init; }
        public string? From { get; init; }
        public string? To { get; init; }
        public string? Text { get; init; }
    }
}
