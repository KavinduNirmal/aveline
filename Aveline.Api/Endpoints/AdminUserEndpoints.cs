using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Aveline-team-only user administration for support (FR-3.7, FR-3.8). These routes are
/// deliberately never reachable with an API key: they use the permission policies, which
/// authenticate with the default bearer scheme only.
/// </summary>
public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/users").WithTags("Admin Users");

        group.MapGet("", async (
            string? q,
            string? state,
            int? page,
            int? pageSize,
            IUserService userService,
            CancellationToken ct) =>
        {
            AccountState? parsedState = null;
            if (!string.IsNullOrWhiteSpace(state))
            {
                if (!Enum.TryParse<AccountState>(state, ignoreCase: true, out var value))
                {
                    return Results.BadRequest(new { message = "state must be OnboardingPending, Active or Suspended." });
                }

                parsedState = value;
            }

            var users = await userService.SearchUsersAsync(q, parsedState, page ?? 1, pageSize ?? 20, ct);
            return Results.Ok(users);
        }).RequireAuthorization(Permissions.AdminUsersRead);

        group.MapPatch("/{userId:guid}/state", async (
            Guid userId,
            ChangeUserStateRequest request,
            ClaimsPrincipal principal,
            IUserService userService,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<AccountState>(request.AccountState, ignoreCase: true, out var state))
            {
                return Results.BadRequest(new { message = "accountState must be OnboardingPending, Active or Suspended." });
            }

            var actorUserId = await ResolveActorUserIdAsync(principal, userService, ct);
            if (actorUserId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var updated = await userService.ChangeAccountStateAsync(userId, state, actorUserId.Value, ct);
                return Results.Ok(updated);
            }
            catch (InvalidAccountStateTransitionException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = "User not found." });
            }
        }).RequireAuthorization(Permissions.AdminUsersManage);

        return endpoints;
    }

    private static async Task<Guid?> ResolveActorUserIdAsync(
        ClaimsPrincipal principal, IUserService userService, CancellationToken ct)
    {
        var clerkId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(clerkId))
        {
            return null;
        }

        var profile = await userService.GetByClerkIdAsync(clerkId, ct);
        return profile?.Id;
    }
}

/// <summary>Request to set an account's lifecycle state (FR-3.8).</summary>
public sealed record ChangeUserStateRequest(string AccountState);
