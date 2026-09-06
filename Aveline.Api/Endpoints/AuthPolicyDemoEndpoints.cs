using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Minimal endpoints demonstrating the role-based and permission-based policies
/// defined in <see cref="AuthorizationConfiguration"/>. Replace these with real
/// feature endpoints as the domain modules are built.
/// </summary>
public static class AuthPolicyDemoEndpoints
{
    public static IEndpointRouteBuilder MapAuthPolicyDemoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/policies").WithTags("Auth Policy Demo");

        group.MapGet("/associate", () => Results.Ok(new { message = "Any staff member can access this." }))
            .RequireAuthorization(AuthorizationConfiguration.AssociatesPolicy);

        group.MapGet("/manager", () => Results.Ok(new { message = "Managers and above can access this." }))
            .RequireAuthorization(AuthorizationConfiguration.ManagersPolicy);

        group.MapGet("/owner", () => Results.Ok(new { message = "Store owners only." }))
            .RequireAuthorization(AuthorizationConfiguration.OwnersPolicy);

        group.MapGet("/approvals/approve", () => Results.Ok(new { message = "Requires the approvals:approve permission." }))
            .RequireAuthorization(Permissions.ApprovalsApprove);

        group.MapGet("/payments/refund", () => Results.Ok(new { message = "Requires the payments:refund permission." }))
            .RequireAuthorization(Permissions.PaymentsRefund);

        // Organization-scoped demo: the caller must hold an active membership in the
        // organization named in the route (enforced server-side, see
        // OrganizationScopeAuthorizationHandler). Cross-organization calls are denied.
        group.MapGet("/orgs/{organizationId:guid}/catalog", (Guid organizationId) =>
                Results.Ok(new { message = "Active boutique member can view this catalog.", organizationId }))
            .RequireAuthorization(AuthorizationConfiguration.BoutiqueAccessPolicy);

        // Deliberately unannotated: proves the fallback policy requires authentication.
        group.MapGet("/fallback/authed-by-default", () =>
            Results.Ok(new { message = "Requires authentication via the fallback policy." }));

        return endpoints;
    }
}
