using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.DTOs;
using Aveline.Api.Modules.Payments.Services;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Shared.Services;

namespace Aveline.Api.Modules.Payments.Endpoints;

/// <summary>
/// The organisation-facing top-up checkout, poll and cancel routes (plan §9.2, Appendix C). The
/// existing operator top-up route (<c>POST /blossoms/top-ups</c>) is untouched: it stays the
/// <c>manual</c> path (decision D7).
/// </summary>
public static class PaymentEndpoints
{
    /// <summary>The currency the price book is denominated in, matching the shipped catalogue route.</summary>
    private const string PriceBookCurrency = "LKR";

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/orgs/{organizationId:guid}").WithTags("Payments");

        group.MapPost("/blossoms/top-ups/checkout", async (
            Guid organizationId,
            CreateTopUpCheckoutRequest request,
            ClaimsPrincipal principal,
            HttpContext http,
            IPaymentIntentService intents,
            IPricingService pricing,
            IUserService users,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            try
            {
                var now = DateTime.UtcNow;
                var priceEntry = PriceBookSelection.SelectActiveSku(
                    await pricing.ListPriceEntriesAsync(
                        BlossomSkuKind.TopUpPack, planTier: null, organizationId: null, ct),
                    request.SkuCode,
                    now);

                if (priceEntry is null)
                {
                    return Results.BadRequest(new { code = "unknown-sku", message = "Unknown top-up SKU." });
                }

                if (priceEntry.PriceLkr <= 0m)
                {
                    // A free pack is not a charge; the provider boundary refuses a non-positive
                    // amount, and saying so here names the SKU rather than the provider's rule.
                    return Results.BadRequest(new
                    {
                        code = "unpurchasable-sku",
                        message = "This top-up pack has no price and cannot be purchased.",
                    });
                }

                // Deliverable 6 / BR-2.6: the cap is resolved **before** payment and persisted on the
                // intent, so a settlement after a period boundary cannot change what was bought.
                var allowCrossPeriod = configuration.GetValue("Billing:AllowCrossPeriodTopUps", false);
                var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var periodEnd = periodStart.AddMonths(1);

                var actorUserId = await ResolveActorUserIdAsync(principal, users, ct);

                var view = await intents.CreateAsync(new CreatePaymentIntentCommand(
                    organizationId,
                    PaymentPurpose.BlossomTopUp,
                    Money.Lkr(priceEntry.PriceLkr),
                    $"Top-up purchase {priceEntry.SkuCode} ({priceEntry.BlossomQuantity} Blossoms).",
                    priceEntry.SkuCode,
                    priceEntry.BlossomQuantity,
                    IdempotencyKey(http),
                    actorUserId,
                    allowCrossPeriod ? null : periodStart,
                    allowCrossPeriod ? null : periodEnd), ct);

                return Results.Created(
                    $"/api/v1/orgs/{organizationId}/payment-intents/{view.PaymentIntentId}",
                    ToTopUpResponse(view));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        })
        .AddEndpointFilter<IdempotencyEndpointFilter>()
        .RequireAuthorization(AuthorizationConfiguration.BillingManagePolicy)
        .WithName("createBlossomTopUpCheckout")
        .WithSummary("Create a payment intent for a Blossom top-up pack")
        .WithDescription(
            "Resolves the active top-up pack from the price book, creates a provider-neutral payment "
            + "intent and returns the provider handoff. The redirect back from a hosted page is not "
            + "proof of settlement: poll `GET /api/v1/orgs/{organizationId}/payment-intents/{id}` for "
            + "the terminal state. Requires `Idempotency-Key`.")
        .Produces<TopUpCheckoutResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status502BadGateway)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/payment-intents/{paymentIntentId:guid}", async (
            Guid organizationId,
            Guid paymentIntentId,
            IPaymentIntentService intents,
            CancellationToken ct) =>
        {
            try
            {
                var view = await intents.GetAsync(organizationId, paymentIntentId, ct);
                return Results.Ok(ToIntentResponse(view));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        })
        .RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy)
        .WithName("getPaymentIntent")
        .WithSummary("Poll one payment intent")
        .WithDescription(
            "The intent's current state for its organisation. An intent belonging to another "
            + "organisation is a 404, and an unsettled intent past its expiry reports `Expired`.")
        .Produces<PaymentIntentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/payment-intents/{paymentIntentId:guid}/cancel", async (
            Guid organizationId,
            Guid paymentIntentId,
            string? reason,
            IPaymentIntentService intents,
            CancellationToken ct) =>
        {
            try
            {
                var view = await intents.CancelAsync(
                    organizationId,
                    paymentIntentId,
                    string.IsNullOrWhiteSpace(reason) ? "Customer abandoned the checkout." : reason,
                    ct);

                return Results.Ok(ToIntentResponse(view));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        })
        .AddEndpointFilter<IdempotencyEndpointFilter>()
        .RequireAuthorization(AuthorizationConfiguration.BillingManagePolicy)
        .WithName("cancelPaymentIntent")
        .WithSummary("Cancel an unpaid payment intent")
        .WithDescription(
            "Voids an unsettled intent at the provider and moves it to `Cancelled`. A settled or "
            + "already-cancelled intent is a 409. Requires `Idempotency-Key`.")
        .Produces<PaymentIntentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .Produces(StatusCodes.Status502BadGateway);

        group.MapPost("/payment-intents/{paymentIntentId:guid}/refund", async (
            Guid organizationId,
            Guid paymentIntentId,
            RefundPaymentIntentRequest request,
            IPaymentIntentService intents,
            CancellationToken ct) =>
        {
            try
            {
                var result = await intents.RequestRefundAsync(
                    organizationId,
                    paymentIntentId,
                    request.AmountLkr,
                    string.IsNullOrWhiteSpace(request.Reason) ? "Customer refund." : request.Reason,
                    ct);

                return Results.Ok(ToRefundResponse(result));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        })
        .AddEndpointFilter<IdempotencyEndpointFilter>()
        // Platform money is refunded by the platform owner: `revenue:refund` is deliberately denied
        // to `admin` (Permissions.PermissionsDeniedToAdmin), per plan §9.6 and docs/api/README.md.
        .RequireAuthorization(Permissions.RevenueRefund)
        .WithName("refundPaymentIntent")
        .WithSummary("Refund a settled payment intent")
        .WithDescription(
            "Returns money the journal recorded collecting. The provider is asked **before** the "
            + "ledger is written (decision D8), and a refund requires a live `Verified` receipt for "
            + "the same `(SourceKind, SourceRef)`. A pending intent is a 409, as is a refund outside "
            + "a configured `Payments:RefundWindowDays`. Requires `Idempotency-Key`.")
        .Produces<PaymentRefundResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented)
        .Produces(StatusCodes.Status502BadGateway);

        return endpoints;
    }

    /// <summary>
    /// The one switch that maps <see cref="PaymentDomainException"/> to its documented status and
    /// code (plan §6.2's table). Anything else is a genuine server fault and is left to the global
    /// exception handler rather than flattened into a plausible response.
    /// </summary>
    internal static IResult MapProblem(Exception exception) => exception switch
    {
        PaymentDomainException domain when domain.StatusCode is { } status && domain.ErrorCode is { } code =>
            Results.Json(new { code, message = domain.Message }, statusCode: status),
        PaymentDomainException domain when domain.StatusCode is { } status =>
            Results.Json(new { message = domain.Message }, statusCode: status),
        // The shipped refund rule is the revenue family's, so its refusal keeps its wire code here
        // rather than being flattened into the payment family's vocabulary.
        RevenueRefundNotAllowedException refund =>
            Results.Json(new { code = refund.Code, message = refund.Message },
                statusCode: StatusCodes.Status409Conflict),
        DuplicateRevenueEntryException duplicate =>
            Results.Json(new { code = duplicate.Code, message = duplicate.Message },
                statusCode: StatusCodes.Status409Conflict),
        _ => throw exception,
    };

    internal static PaymentRefundResponse ToRefundResponse(PaymentRefundResult result) =>
        new(
            result.Intent.PaymentIntentId,
            result.Intent.Provider,
            result.Intent.Status,
            result.ProviderRefundId,
            result.LedgerEntryId,
            result.AmountLkr,
            result.Intent.Currency,
            result.Intent.RefundedAt);

    internal static TopUpCheckoutResponse ToTopUpResponse(PaymentIntentView view) =>
        new(
            view.PaymentIntentId,
            view.Provider,
            view.Status,
            view.SkuCode ?? string.Empty,
            view.BlossomQuantity ?? 0m,
            view.AmountLkr,
            view.Currency,
            view.CheckoutUrl,
            view.ExpiresAt);

    internal static PaymentIntentResponse ToIntentResponse(PaymentIntentView view) =>
        new(
            view.PaymentIntentId,
            view.Provider,
            view.ProviderIntentId ?? string.Empty,
            view.Purpose.ToString(),
            view.Status,
            view.AmountLkr,
            view.Currency,
            view.CheckoutUrl,
            view.FailureCode,
            view.FailureMessage,
            view.CreatedAt,
            view.SettledAt,
            view.ExpiresAt);

    private static string? IdempotencyKey(HttpContext http)
    {
        var value = http.Request.Headers[IdempotencyEndpointFilter.HeaderName].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static async Task<Guid> ResolveActorUserIdAsync(
        ClaimsPrincipal principal, IUserService users, CancellationToken ct)
    {
        var clerkId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(clerkId))
        {
            return Guid.Empty;
        }

        var user = await users.GetByClerkIdAsync(clerkId, ct);
        return user?.Id ?? Guid.Empty;
    }
}
