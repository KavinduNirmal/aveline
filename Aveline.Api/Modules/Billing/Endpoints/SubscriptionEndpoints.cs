using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Endpoints;
using Aveline.Api.Modules.Shared.Services;

namespace Aveline.Api.Modules.Billing.Endpoints;

public sealed record ChangePlanRequest(PlanTier PlanTier, string? Effective, string? Reason);

public sealed record CancelSubscriptionRequest(string? Reason);

/// <summary>
/// Subscription, plan-change and entitlement endpoints (docs/api/README.md §C.3).
/// </summary>
public static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var subscription = endpoints.MapGroup("/orgs/{organizationId:guid}/subscription")
            .WithTags("Subscription");

        subscription.MapGet("", async (
            Guid organizationId, ISubscriptionService subscriptions, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await subscriptions.GetSubscriptionAsync(organizationId, ct));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy);

        subscription.MapPost("/change-plan", async (
            Guid organizationId,
            ChangePlanRequest request,
            ClaimsPrincipal principal,
            HttpContext http,
            ISubscriptionService subscriptions,
            IUserService users,
            CancellationToken ct) =>
        {
            try
            {
                var actorUserId = await ResolveActorUserIdAsync(principal, users, ct);
                var result = await subscriptions.ChangePlanAsync(new ChangePlanCommand(
                    organizationId,
                    request.PlanTier,
                    request.Effective,
                    request.Reason,
                    actorUserId,
                    IdempotencyKey(http),
                    "org.subscription.change-plan"), ct);

                return Results.Ok(result);
            }
            catch (Exception exception)
            {
                return MapProblem(exception, http);
            }
        })
        .AddEndpointFilter<IdempotencyEndpointFilter>()
        .RequireAuthorization(AuthorizationConfiguration.BillingManagePolicy);

        subscription.MapPost("/cancel", async (
            Guid organizationId,
            CancelSubscriptionRequest request,
            ISubscriptionService subscriptions,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await subscriptions.CancelAsync(organizationId, request.Reason, ct));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(AuthorizationConfiguration.BillingManagePolicy);

        // Plan §9.5(c): the un-cancel path is only meaningful where the provider supports it, so a
        // provider without `SupportsCancelAtPeriodEnd` answers `501 payment-provider-capability-missing`.
        subscription.MapPost("/resume", async (
            Guid organizationId,
            ISubscriptionService subscriptions,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await subscriptions.ResumeAsync(organizationId, ct));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        })
        .RequireAuthorization(AuthorizationConfiguration.BillingManagePolicy)
        .WithName("resumeSubscription")
        .WithSummary("Withdraw a scheduled subscription cancellation")
        .WithDescription(
            "Clears `cancelAtPeriodEnd` and asks the provider to restore its recurring agreement. "
            + "Only meaningful where the provider's capabilities include cancellation-at-period-end; "
            + "otherwise the provider cannot be asked, and the response is "
            + "`501 payment-provider-capability-missing`.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented);

        var entitlements = endpoints.MapGroup("/orgs/{organizationId:guid}/entitlements")
            .WithTags("Entitlements");

        entitlements.MapGet("", async (
            Guid organizationId, ISubscriptionService subscriptions, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await subscriptions.GetEntitlementsAsync(organizationId, ct));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy);

        entitlements.MapGet("/usage", async (
            Guid organizationId, ISubscriptionService subscriptions, CancellationToken ct) =>
        {
            try
            {
                var usage = await subscriptions.GetEntitlementUsageAsync(organizationId, ct);
                return Results.Ok(new
                {
                    items = usage.Items,
                    materialisedAt = usage.MaterialisedAt,
                    dataQuality = new { materialisedCounts = usage.MaterialisedCounts },
                });
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy);

        return endpoints;
    }

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

    private static IResult MapProblem(Exception exception, HttpContext? http = null) => exception switch
    {
        BlossomOrganizationNotFoundException notFound => Results.NotFound(new { message = notFound.Message }),
        PlanLimitViolationException violation => HandlePlanViolation(violation, http),
        NoOpPlanChangeException noOp => Results.BadRequest(new { code = "no-op-plan-change", message = noOp.Message }),
        BlossomValidationException validation => Results.BadRequest(new { message = validation.Message }),
        PeriodClosedException periodClosed => Results.Conflict(new
        {
            code = "period-closed",
            message = periodClosed.Message,
        }),
        // Plan §9.3 F3 lets the provider price an upgrade's proration charge, so this route can now
        // surface the payment family's documented failures (for example
        // 501 payment-provider-capability-missing). One mapping, shared with the payment routes.
        PaymentDomainException payment => PaymentEndpoints.MapProblem(payment),
        _ => throw exception,
    };

    private static IResult HandlePlanViolation(PlanLimitViolationException violation, HttpContext? http)
    {
        if (http is not null)
        {
            var firstKey = violation.Violations.FirstOrDefault()?.Key ?? "plan_limit";
            http.Items["TelemetryErrorCode"] = $"violation_{firstKey}";
            http.Response.Headers["X-Error-Code"] = "plan-limit-violation";
            http.Response.Headers["X-Violated-Keys"] = string.Join(",", violation.Violations.Select(v => v.Key));
        }

        return Results.Conflict(new
        {
            code = "plan-limit-violation",
            message = violation.Message,
            violations = violation.Violations.Select(v => new { key = v.Key, observed = v.Observed, allowed = v.Allowed }),
        });
    }
}
