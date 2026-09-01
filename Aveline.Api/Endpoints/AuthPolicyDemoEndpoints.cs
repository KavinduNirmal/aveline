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

        return endpoints;
    }
}
