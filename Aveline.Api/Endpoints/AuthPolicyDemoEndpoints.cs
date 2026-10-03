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
            .RequireAuthorization(AuthorizationConfiguration.StaffAccessPolicy);

        // The manager/owner demonstrations that used to live here were removed: neither route has
        // an organization segment to scope against, and the roles they named can only arrive
        // through the Clerk `org_role` claim, which this deployment never populates. A route that
        // needs "manager of this boutique" belongs on `/orgs/{organizationId}/…` with an
        // org-scoped policy, where the membership is the source of truth. The Managers/Owners
        // policies themselves remain defined and unit-tested for platform callers.

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

        // Development-only probe for the hardening tests: a real thrown exception proves the
        // global exception handler still emits the security headers on a handled 500 (§3.6).
        group.MapGet("/fallback/unhandled", (HttpContext _) =>
        {
            throw new InvalidOperationException("Deliberate Development-only failure probe.");
        });

        return endpoints;
    }
}
