using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.Home.DTOs;
using Aveline.Api.Modules.Home.Services;
using Aveline.Api.Modules.Shared.Repositories;

namespace Aveline.Api.Modules.Home.Endpoints;

/// <summary>
/// Home's focus surface: the derived feed and the dismissal record that makes a
/// sign-off an act rather than a local list edit.
/// </summary>
/// <remarks>
/// Both routes are org-scoped by <c>{organizationId:guid}</c> and authorized by
/// the named <see cref="AuthorizationConfiguration.BoutiqueAccessPolicy"/> policy,
/// which carries an <c>OrganizationScopeRequirement</c>: the caller must be an
/// active member of that organization. The feed is deliberately readable by every
/// staff role (<c>catalog:view</c>); the client row, which needs
/// <c>customers:view</c>, lives on the shared customer surface.
/// </remarks>
public static class HomeEndpoints
{
    public static IEndpointRouteBuilder MapHomeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var stats = endpoints.MapGroup("/orgs/{organizationId:guid}/stats").WithTags("Home");
        stats.MapGet("/home", async (
            Guid organizationId,
            ClaimsPrincipal principal,
            IFocusFeedService feed,
            IUserRepository users,
            CancellationToken ct) =>
        {
            var userId = await ResolveUserIdAsync(principal, users, ct);
            var canReadAgentApprovals = HasAgentStatistics(principal);
            return Results.Ok(await feed.GetFeedAsync(
                organizationId, userId, canReadAgentApprovals, ct));
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueAccessPolicy);

        var focus = endpoints.MapGroup("/orgs/{organizationId:guid}/focus").WithTags("Home");
        focus.MapPost("/dismissals", async (
            Guid organizationId,
            DismissFocusTaskRequest request,
            ClaimsPrincipal principal,
            IFocusFeedService feed,
            IFocusDismissalService dismissals,
            IUserRepository users,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.SourceKey)
                || string.IsNullOrWhiteSpace(request.Domain)
                || string.IsNullOrWhiteSpace(request.Decision))
            {
                return Results.BadRequest(new
                {
                    message = "sourceKey, domain and decision are required.",
                });
            }

            var userId = await ResolveUserIdAsync(principal, users, ct);
            var canReadAgentApprovals = HasAgentStatistics(principal);

            // A dismissal must name a docket that is actually on this caller's
            // floor. A docket from another organization is therefore
            // indistinguishable from one that does not exist.
            var current = await feed.GetFeedAsync(organizationId, userId, canReadAgentApprovals, ct);
            var item = current.Items.FirstOrDefault(candidate =>
                candidate.Domain == request.Domain && candidate.SourceKey == request.SourceKey);
            if (item is null)
            {
                return Results.NotFound(new { message = "That docket is not on your floor." });
            }

            var dismissal = await dismissals.RecordAsync(
                organizationId, userId, item, request.Decision, request.Note, ct);

            // The client refreshes its counts from this payload rather than
            // trusting its own optimistic removal.
            var after = await feed.GetFeedAsync(organizationId, userId, canReadAgentApprovals, ct);
            return Results.Ok(new FocusDismissalResponseDto(
                dismissal.Id,
                item.SourceKey,
                item.Domain,
                dismissal.Decision,
                dismissal.DismissedAtUtc,
                after.Counts));
        })
            .RequireAuthorization(AuthorizationConfiguration.BoutiqueAccessPolicy)
            .AddEndpointFilter<IdempotencyEndpointFilter>();

        return endpoints;
    }

    private static async Task<Guid> ResolveUserIdAsync(
        ClaimsPrincipal principal, IUserRepository users, CancellationToken ct)
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

    /// <summary>
    /// Whether the caller's role claims grant <c>stats:view:agent</c>. The commerce
    /// domain is not hidden from a role that lacks it by a 403; it is simply not
    /// derived, and <c>dataQuality.commerceAvailable</c> says so.
    /// </summary>
    private static bool HasAgentStatistics(ClaimsPrincipal principal)
    {
        foreach (var claim in principal.FindAll("user_role").Concat(principal.FindAll("org_role")))
        {
            if (Permissions.IsGranted(claim.Value, Permissions.StatsViewAgent))
            {
                return true;
            }
        }

        return false;
    }
}
