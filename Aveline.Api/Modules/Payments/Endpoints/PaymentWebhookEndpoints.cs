using System.Text;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Services;

namespace Aveline.Api.Modules.Payments.Endpoints;

/// <summary>
/// The anonymous provider callback (plan §9.2, Appendix C). The raw body is read **before** any JSON
/// binding, exactly as the Clerk webhook verifier does, because the signature is computed over the
/// bytes as they arrived.
/// </summary>
public static class PaymentWebhookEndpoints
{
    public static IEndpointRouteBuilder MapPaymentWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/webhooks/payments/{provider:alpha}", async (
            string provider,
            HttpContext http,
            IPaymentProviderEventService providerEvents,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            // Raw body first: a binder would consume the stream, and a re-serialised body is not the
            // bytes the provider signed.
            http.Request.EnableBuffering();
            string rawBody;
            using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true))
            {
                rawBody = await reader.ReadToEndAsync(ct);
            }

            http.Request.Body.Position = 0;

            var headers = http.Request.Headers.ToDictionary(
                header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            var request = new PaymentWebhookRequest(
                rawBody,
                headers,
                http.Connection.RemoteIpAddress?.ToString(),
                clock.GetUtcNow());

            try
            {
                var outcome = await providerEvents.IngestAsync(provider, request, ct);
                return Results.Ok(new
                {
                    received = true,
                    duplicate = !outcome.IsNew,
                    processed = outcome.Processed,
                });
            }
            catch (PaymentWebhookVerificationException)
            {
                // 403 with an empty body, matching the WhatsApp webhook convention: an attacker is
                // not told which part of the verification failed, and the adapter has already
                // recorded the reason against the verification-failure metric.
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (PaymentDomainException domain)
            {
                return PaymentEndpoints.MapProblem(domain);
            }
        })
        .AllowAnonymous()
        .WithTags("Payments")
        .WithName("receivePaymentProviderWebhook")
        .WithSummary("Receive a payment provider callback")
        .WithDescription(
            "Anonymous and signature-verified. The provider's adapter authenticates the raw body and "
            + "its timestamp; an invalid signature or a stale timestamp is a 403 with an empty body. "
            + "A duplicate `(Provider, ProviderEventId)` is accepted and returns 200 with "
            + "`duplicate: true`; the first delivery settles the intent.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }
}
