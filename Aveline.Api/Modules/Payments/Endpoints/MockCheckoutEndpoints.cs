using System.Net;
using System.Text;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Providers;
using Aveline.Api.Modules.Payments.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Payments.Endpoints;

/// <summary>
/// The Development-only mock checkout page and settle endpoint (plan §7.2, §7.4 guardrail 5). Mapped
/// only inside <c>app.Environment.IsDevelopment()</c>, so they do not exist in a Production build
/// even if the provider is misconfigured.
/// </summary>
/// <remarks>
/// The page writes nothing: it renders one form per documented scenario token, each of which posts
/// the token as a query-string value. There is no form field for a card, because the mock's
/// credential is a closed test-token table and never persisted (constraint C11).
/// </remarks>
public static class MockCheckoutEndpoints
{
    public static IEndpointRouteBuilder MapMockCheckoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/dev/mock-checkout").WithTags("Payments");

        group.MapGet("/{intentId:guid}", (Guid intentId, IOptions<PaymentsOptions> options) =>
            Results.Content(RenderPage(intentId, options.Value.Mock), "text/html; charset=utf-8"))
            .AllowAnonymous()
            .WithName("getMockCheckoutPage")
            .WithSummary("The mock provider's hosted checkout page (Development only)")
            .Produces(StatusCodes.Status200OK, contentType: "text/html");

        group.MapPost("/{intentId:guid}/settle", async (
            Guid intentId,
            string? token,
            AppDbContext db,
            IPaymentProviderFactory providers,
            IPaymentProviderEventService providerEvents,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var intent = await db.PaymentIntents.FirstOrDefaultAsync(row => row.Id == intentId, ct);
            if (intent is null)
            {
                return Results.NotFound(new { message = $"No payment intent '{intentId}'." });
            }

            if (string.IsNullOrWhiteSpace(intent.ProviderIntentId)
                || providers.Resolve(intent.Provider) is not MockPaymentProvider mock)
            {
                return Results.BadRequest(new
                {
                    code = "not-a-mock-intent",
                    message = "This intent was not created by the mock provider.",
                });
            }

            var credential = string.IsNullOrWhiteSpace(token)
                ? MockPaymentProvider.SucceedToken
                : token;

            try
            {
                // The customer's choice lands on the provider-side charge first, exactly as it would
                // on a real hosted page ...
                var applied = await mock.ApplyTestCredentialAsync(
                    intent.ProviderIntentId, credential, ct);

                // ... and then the mock "calls back" through the real webhook path, so the demo
                // exercises verification, the inbox and the settlement rather than a shortcut.
                var payload = mock.BuildWebhook(intent.ProviderIntentId, credential);
                var outcome = await providerEvents.IngestAsync(
                    intent.Provider, payload.ToRequest(clock.GetUtcNow()), ct);

                return Results.Ok(new
                {
                    paymentIntentId = intent.Id,
                    providerStatus = applied.Status.ToString(),
                    settled = outcome.Processed,
                    duplicate = !outcome.IsNew,
                });
            }
            catch (Exception exception)
            {
                return PaymentEndpoints.MapProblem(exception);
            }
        })
        .AllowAnonymous()
        .WithName("settleMockCheckout")
        .WithSummary("Complete a mock checkout with a test credential (Development only)")
        .WithDescription(
            "Applies a documented mock test credential to the intent and then delivers the mock's "
            + "signed webhook, so the whole settlement path runs. `token` is a query-string value from "
            + "the closed test table; no card data is accepted or stored.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static string RenderPage(Guid intentId, MockProviderOptions options)
    {
        var builder = new StringBuilder();
        builder.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.Append("<title>Mock payment checkout</title>");
        builder.Append("<style>body{font-family:system-ui,sans-serif;margin:2rem;max-width:40rem}");
        builder.Append("code{background:#f4f4f4;padding:.1rem .3rem}form{display:inline-block;margin:.2rem}");
        builder.Append("button{padding:.5rem .75rem;margin:.15rem}</style></head><body>");
        builder.Append("<h1>Mock payment checkout</h1>");
        builder.Append("<p>No real money can be collected. This page exists only in Development. ");
        builder.Append("Payment intent <code>").Append(intentId).Append("</code>.</p>");
        builder.Append("<p>Choose a documented test credential:</p>");

        foreach (var token in MockPaymentProvider.ScenarioTokens)
        {
            builder.Append("<form method=\"post\" action=\"/api/v1/dev/mock-checkout/")
                .Append(intentId)
                .Append("/settle?token=")
                .Append(WebUtility.UrlEncode(token))
                .Append("\"><button type=\"submit\">")
                .Append(WebUtility.HtmlEncode(token))
                .Append("</button></form>");
        }

        builder.Append("<p>Stripe-compatible test cards are accepted by the settle endpoint's ");
        builder.Append("<code>token</code> value too, for example <code>4242424242424242</code>.</p>");
        builder.Append("<p>Checkout base URL: <code>")
            .Append(WebUtility.HtmlEncode(options.CheckoutBaseUrl))
            .Append("</code></p>");
        builder.Append("</body></html>");

        return builder.ToString();
    }
}
