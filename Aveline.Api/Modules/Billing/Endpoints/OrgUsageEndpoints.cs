using Aveline.Api.Configurations;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Modules.Billing.Endpoints;

/// <summary>
/// Owner/manager-visible billing endpoints scoped to an organization. Unlike the internal
/// <see cref="UsageEndpoints"/> (internal-token only), these are reachable by authenticated
/// boutique members so the tenant dashboard can surface a Blossom balance.
/// </summary>
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
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueAccessPolicy);

        return endpoints;
    }
}
