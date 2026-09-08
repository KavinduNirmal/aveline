using System.Security.Claims;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Aveline.Api.Modules.Notifications.Hubs;

/// <summary>
/// Real-time notification hub at <c>/hubs/notifications</c>. Authenticated clients
/// (React web + Flutter app) connect here to receive <c>ReceiveNotification</c> messages.
///
/// <para>
/// <b>Client contract (issues #92/#93 consume this):</b>
/// </para>
/// <list type="bullet">
///   <item>Hub path: <c>/hubs/notifications</c>.</item>
///   <item>Auth: Clerk JWT passed as <c>?access_token=</c> (SignalR query-string convention).</item>
///   <item>Server→client method: <c>ReceiveNotification</c>; payload is a
///     <see cref="NotificationDto"/> serialized as JSON:
///     <c>{ "type": "...", "title": "...", "body": "...", "data": { ... } }</c>.</item>
/// </list>
///
/// <para>
/// On connect the user is added to <c>user:&#123;userId&#125;</c> (always) and
/// <c>org:&#123;organizationId&#125;</c> (per active membership) so the dispatcher can
/// target a single user or a whole organization. <c>userId</c> is the Aveline
/// <see cref="Aveline.Api.Modules.Shared.Models.User.Id"/> (a Guid), matching the
/// <c>ResolvedRecipient.UserId</c> the realtime channel sends to.
/// </para>
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    private readonly IUserRepository _users;
    private readonly IOrganizationRepository _organizations;

    public NotificationHub(IUserRepository users, IOrganizationRepository organizations)
    {
        _users = users;
        _organizations = organizations;
    }

    public override async Task OnConnectedAsync()
    {
        var clerkId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? Context.User?.FindFirstValue("sub");

        // [Authorize] normally guarantees an authenticated principal; this is a defensive
        // guard so an unexpected anonymous context fails loudly rather than joining groups.
        if (string.IsNullOrWhiteSpace(clerkId))
        {
            throw new HubException("Unauthenticated notification hub connection.");
        }

        var user = await _users.GetByClerkIdAsync(clerkId, Context.ConnectionAborted);
        if (user is null)
        {
            throw new HubException("Authenticated user does not exist in Aveline.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName.ForUser(user.Id), Context.ConnectionAborted);

        var memberships = await _organizations.ListMembershipsForUserAsync(user.Id, Context.ConnectionAborted);
        foreach (var membership in memberships)
        {
            if (membership.Status == MembershipStatus.Active)
            {
                await Groups.AddToGroupAsync(
                    Context.ConnectionId,
                    GroupName.ForOrganization(membership.OrganizationId),
                    Context.ConnectionAborted);
            }
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Canonical SignalR group names used by the hub (on connect) and the realtime channel
/// (on send). Kept in one place so the two sides can never drift apart.
/// </summary>
public static class GroupName
{
    /// <summary>Group receiving all notifications for a single Aveline user.</summary>
    public static string ForUser(Guid userId) => $"user:{userId}";

    /// <summary>Group receiving all notifications broadcast to an organization.</summary>
    public static string ForOrganization(Guid organizationId) => $"org:{organizationId}";
}
