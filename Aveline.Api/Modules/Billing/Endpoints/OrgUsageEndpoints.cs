using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.DTOs;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Modules.Billing.Endpoints;

/// <summary>
/// Owner/manager-visible billing endpoints scoped to an organization. Unlike the internal
/// <see cref="UsageEndpoints"/> (internal-token only), these are reachable by authenticated
/// boutique members so the tenant dashboard can surface a Blossom balance.
/// </summary>
/// <remarks>
/// <c>GET …/usage</c> takes <see cref="AuthorizationConfiguration.BoutiqueBillingSelfViewPolicy"/>
/// (<c>billing:view:self</c>), not <c>BoutiqueAccess</c> (<c>catalog:view</c>). The previous gate
/// contradicted this file's own doc comment — a usage read is a *billing* read, and borrowing a
/// catalog permission for it is how <c>catalog:manage</c> became impossible to enforce without also
/// taking the camera away from staff. Every org role holds <c>billing:view:self</c>, so the
/// effective access is unchanged.
/// </remarks>
public static class OrgUsageEndpoints
{
    public static IEndpointRouteBuilder MapOrgUsageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var orgGroup = endpoints.MapGroup("/orgs");

        orgGroup.MapGet("/{organizationId:guid}/usage", async (
            Guid organizationId,
            IUsageTrackerService usageTracker,
            CancellationToken ct) =>
        {
            var summary = await usageTracker.GetUsageSummaryAsync(organizationId, ct);
            return Results.Ok(summary);
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueBillingSelfViewPolicy);

        // E-12. The billing-period history. `billing:view` is the same gate as the statement this
        // panel sits beside (manager and owner; a supervisor holds only `billing:view:self`), so a
        // reader who may see what a period consumed may see its history.
        orgGroup.MapGet("/{organizationId:guid}/billing/periods", async (
            Guid organizationId,
            int? take,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (take is { } requested
                && (requested < TenantBillingReadService.MinTake || requested > TenantBillingReadService.MaxTake))
            {
                return Results.BadRequest(new
                {
                    message = $"take must be between {TenantBillingReadService.MinTake} "
                              + $"and {TenantBillingReadService.MaxTake}.",
                });
            }

            var periods = await TenantBillingReadService.GetPeriodsAsync(
                db, organizationId, take ?? TenantBillingReadService.DefaultTake, ct);

            return Results.Ok(periods);
        })
        .WithName("getTenantBillingPeriods")
        .WithSummary("List the boutique's billing-period history")
        .WithDescription(
            "The most recent billing periods, newest first: the period's Blossom account (limit, "
            + "granted, adjusted, used, remaining), the top-ups that landed in it, and the plan. "
            + "`planListPriceLkr` is `null` — never `0` — whenever no price is configured, with "
            + "`subscriptionPricesConfigured` saying so. Requires `billing:view`.")
        .Produces<IReadOnlyList<BillingPeriodDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy);

        return endpoints;
    }
}
