using System.Security.Claims;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Admin.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Administrator access requests (issue #59): an admin sign-up submits after
/// email verification, and an existing administrator reviews the request. Approval
/// grants the <c>admin</c> role via the Clerk Backend API.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin").WithTags("Admin Access");

        group.MapPost("/requests", async (
            ClaimsPrincipal principal,
            IAdminApprovalService adminApprovalService,
            CancellationToken ct) =>
        {
            var clerkId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? principal.FindFirstValue("sub");
            if (string.IsNullOrEmpty(clerkId))
            {
                return Results.Unauthorized();
            }

            var request = await adminApprovalService.SubmitAsync(new SubmitAdminRequestInput(
                clerkId,
                principal.FindFirstValue("email") ?? string.Empty,
                principal.FindFirstValue("first_name") ?? principal.FindFirstValue(ClaimTypes.GivenName) ?? string.Empty,
                principal.FindFirstValue("last_name") ?? principal.FindFirstValue(ClaimTypes.Surname) ?? string.Empty), ct);

            return Results.Ok(ToSummary(request));
        }).RequireAuthorization();

        group.MapGet("/requests", async (
            IAdminApprovalService adminApprovalService,
            CancellationToken ct) =>
        {
            var requests = await adminApprovalService.ListPendingAsync(ct);
            return Results.Ok(requests.Select(ToSummary));
        }).RequireAuthorization(AuthorizationConfiguration.AdminReviewPolicy);

        group.MapPost("/requests/{requestId:guid}/approve", async (
            Guid requestId,
            ClaimsPrincipal principal,
            IAdminApprovalService adminApprovalService,
            CancellationToken ct) =>
        {
            var reviewer = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? principal.FindFirstValue("sub") ?? string.Empty;
            try
            {
                var request = await adminApprovalService.ApproveAsync(requestId, reviewer, ct);
                return Results.Ok(ToSummary(request));
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        }).RequireAuthorization(AuthorizationConfiguration.AdminReviewPolicy);

        group.MapPost("/requests/{requestId:guid}/reject", async (
            Guid requestId,
            ClaimsPrincipal principal,
            IAdminApprovalService adminApprovalService,
            CancellationToken ct) =>
        {
            var reviewer = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? principal.FindFirstValue("sub") ?? string.Empty;
            try
            {
                var request = await adminApprovalService.RejectAsync(requestId, reviewer, ct);
                return Results.Ok(ToSummary(request));
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
        }).RequireAuthorization(AuthorizationConfiguration.AdminReviewPolicy);

        return endpoints;
    }

    private static object ToSummary(Aveline.Api.Modules.Admin.Models.AdminApprovalRequest request) => new
    {
        request.Id,
        request.ClerkUserId,
        request.Email,
        request.FirstName,
        request.LastName,
        request.Status,
        request.RequestedAt,
        request.ReviewedAt,
        request.ReviewedByClerkUserId,
    };
}
