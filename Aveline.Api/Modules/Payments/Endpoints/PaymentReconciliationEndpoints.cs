using Aveline.Api.Configurations;
using Aveline.Api.Modules.Payments.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Modules.Payments.Endpoints;

/// <summary>
/// S-64 — the cross-org payment reconciliation read (plan §10 Phase 7, §18 Appendix C).
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <c>BlossomReconciliationEndpoints</c>, built the same way. The formula is
/// deliberately **not** reimplemented here: it calls
/// <see cref="IPaymentReconciliationService.ReconcileAsync"/>, the one derivation
/// <c>PaymentReconciliationMetricCollectorJob</c> publishes
/// <c>aveline.payment.unreconciled_intents</c> from. A second derivation would let the console and
/// the alarm disagree about the same intent, which is the one outcome this surface must never
/// produce.
/// </para>
/// <para>
/// Gated <c>stats:system</c>, matching the rest of the <c>/admin/statistics</c> family and
/// deliberately **not** <c>revenue:read</c>: a moderator reads revenue, and this is system
/// statistics.
/// </para>
/// </remarks>
public static class PaymentReconciliationEndpoints
{
    public const string Tag = "Admin Payment Statistics";

    public static IEndpointRouteBuilder MapPaymentReconciliationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        // The full `/api/v1` prefix, mapped at the app root for the same reason the Blossom sibling
        // is: `PaymentsModule` is registered at the root, outside the versioned group.
        var group = endpoints.MapGroup("/api/v1/admin/statistics/payments")
            .WithTags(Tag)
            .RequireAuthorization(AuthorizationConfiguration.StatsSystemPolicy);

        group.MapGet("/reconciliation", GetReconciliationAsync)
            .WithName("getPaymentReconciliation")
            .WithSummary("Read the payment intents whose provider state is unconfirmed")
            .WithDescription(
                "S-64. Every payment intent in the window whose provider state disagrees with the "
                + "stored row -- a charge the provider settled that the webhook never moved, a "
                + "differing status, amount or currency, an unknown charge, a refund recorded on one "
                + "side only -- plus the inbound provider events still unprocessed, by provider. Uses "
                + "the same derivation the `aveline.payment.unreconciled_intents` and "
                + "`aveline.payment.webhook.unprocessed_backlog` gauges are published from. "
                + "Requires `stats:system`.")
            .Produces<PaymentReconciliationReport>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> GetReconciliationAsync(
        [FromQuery] Guid? organizationId,
        [FromQuery] string? provider,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        IPaymentReconciliationService reconciliation,
        CancellationToken ct)
    {
        // The service owns the derivation; the route owns the wire contract. An inverted window is a
        // caller error, not something to silently reinterpret.
        if (from is { } lower && to is { } upper && lower > upper)
        {
            return Results.BadRequest(new
            {
                code = "invalid-window",
                message = "`from` must not be after `to`.",
            });
        }

        var report = await reconciliation.ReconcileAsync(
            new PaymentReconciliationQuery(organizationId, provider, from, to), ct);

        return Results.Ok(report);
    }
}
