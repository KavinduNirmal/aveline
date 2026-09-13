using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;
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
                return MapProblem(exception);
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

    private static IResult MapProblem(Exception exception) => exception switch
    {
        BlossomOrganizationNotFoundException notFound => Results.NotFound(new { message = notFound.Message }),
        PlanLimitViolationException violation => Results.Conflict(new
        {
            code = "plan-limit-violation",
            message = violation.Message,
            violations = violation.Violations.Select(v => new { key = v.Key, observed = v.Observed, allowed = v.Allowed }),
        }),
        NoOpPlanChangeException noOp => Results.BadRequest(new { code = "no-op-plan-change", message = noOp.Message }),
        BlossomValidationException validation => Results.BadRequest(new { message = validation.Message }),
        PeriodClosedException periodClosed => Results.Conflict(new
        {
            code = "period-closed",
            message = periodClosed.Message,
        }),
        _ => throw exception,
    };
}
