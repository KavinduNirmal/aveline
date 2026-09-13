using System.Text;
using System.Text.Json;
using Aveline.Api.Modules.Organizations.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Clerk webhook receiver (<c>POST /api/v1/webhooks/clerk</c>). Clerk has no Aveline
/// token, so the endpoint is anonymous and authenticated by the Svix signature over the
/// raw body. A missing secret fails closed with 503 rather than trusting the caller.
/// </summary>
public static class ClerkWebhookEndpoints
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapClerkWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/webhooks/clerk", async (
            HttpContext httpContext,
            IConfiguration configuration,
            IClerkWebhookSyncService syncService,
            CancellationToken ct) =>
        {
            var secret = configuration["Clerk:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(secret))
            {
                return Results.Json(
                    new { message = "Clerk webhooks are not configured." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            string body;
            using (var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync(ct);
            }

            var bytes = Encoding.UTF8.GetBytes(body);
            var valid = ClerkWebhookVerifier.Verify(
                secret,
                httpContext.Request.Headers["svix-id"],
                httpContext.Request.Headers["svix-timestamp"],
                httpContext.Request.Headers["svix-signature"],
                bytes,
                Tolerance,
                DateTimeOffset.UtcNow);

            if (!valid)
            {
                return Results.Json(
                    new { message = "Invalid webhook signature." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { message = "Malformed webhook payload." });
            }

            using (document)
            {
                var root = document.RootElement;
                var eventType = root.TryGetProperty("type", out var type) ? type.GetString() : null;
                if (string.IsNullOrWhiteSpace(eventType) || !root.TryGetProperty("data", out var data))
                {
                    return Results.BadRequest(new { message = "The webhook payload is missing type or data." });
                }

                await syncService.HandleAsync(eventType, data, ct);
            }

            return Results.Ok(new { received = true });
        }).AllowAnonymous();

        return endpoints;
    }
}
