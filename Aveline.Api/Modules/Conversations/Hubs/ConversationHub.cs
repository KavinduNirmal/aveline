using System.Security.Claims;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Aveline.Api.Modules.Conversations.Hubs;

/// <summary>
/// Real-time conversation hub at <c>/hubs/conversations</c>. Authenticated clients (React
/// web + Flutter app) connect here to receive <c>ReceiveMessage</c> messages for the Salon.
///
/// <para>
/// <b>Client contract:</b>
/// </para>
/// <list type="bullet">
///   <item>Hub path: <c>/hubs/conversations</c>.</item>
///   <item>Auth: Clerk JWT passed as <c>?access_token=</c> (SignalR query-string convention).</item>
///   <item>Server→client methods: <c>ReceiveMessage</c> (a <c>MessageDto</c>),
///     <c>ReceiveAgentState</c> (an <c>AgentStateDto</c>), and
///     <c>ReceiveConversationChanged</c> (a <c>ConversationDto</c> - the inbox tile, so an open
///     list updates without a re-list). The tile is sent to <c>org:&#123;organizationId&#125;</c>
///     for organization-shared threads and to <c>user:&#123;ownerUserId&#125;</c> for a per-user
///     general Salon, so a colleague's private thread never reaches the org group.</item>
///   <item>Client→server: <c>JoinSalon(organizationId, conversationId)</c> to receive messages
///     for a specific Salon. The inbox does not need it: the org and user groups are joined on
///     connect.</item>
/// </list>
///
/// <para>
/// On connect the user is added to <c>user:&#123;userId&#125;</c> and <c>org:&#123;organizationId&#125;</c>
/// (per active membership). <c>JoinSalon</c> verifies the caller is an active member of the
/// organization before adding them to <c>salon:&#123;conversationId&#125;</c>.
/// </para>
/// </summary>
[Authorize]
public class ConversationHub : Hub
{
    private readonly IUserRepository _users;
    private readonly IOrganizationRepository _organizations;
    private readonly IConversationRepository _conversations;

    public ConversationHub(
        IUserRepository users,
        IOrganizationRepository organizations,
        IConversationRepository conversations)
    {
        _users = users;
        _organizations = organizations;
        _conversations = conversations;
    }

    public override async Task OnConnectedAsync()
    {
        var clerkId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? Context.User?.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(clerkId))
        {
            throw new HubException("Unauthenticated conversation hub connection.");
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

    /// <summary>
    /// Joins the caller to the <c>salon:&#123;conversationId&#125;</c> group so they receive
    /// <c>ReceiveMessage</c> for that conversation. The caller must be an active member of the
    /// organization that owns the conversation, and must be allowed to see it: organization-shared
    /// Salons, or their own general Salon (ADR-021).
    /// </summary>
    public async Task JoinSalon(Guid organizationId, Guid conversationId)
    {
        var clerkId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? Context.User?.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(clerkId))
        {
            throw new HubException("Unauthenticated conversation hub connection.");
        }

        var user = await _users.GetByClerkIdAsync(clerkId, Context.ConnectionAborted);
        if (user is null)
        {
            throw new HubException("Authenticated user does not exist in Aveline.");
        }

        var membership = await _organizations.GetMembershipAsync(organizationId, user.Id, Context.ConnectionAborted);
        if (membership is null || membership.Status != MembershipStatus.Active)
        {
            throw new HubException("User is not an active member of this organization.");
        }

        // Organization membership is not enough: the general Salon is owned by one user, so a
        // colleague must not be able to subscribe to it (ADR-021).
        var conversation = await _conversations.GetVisibleToUserAsync(
            organizationId, conversationId, user.Id, Context.ConnectionAborted);
        if (conversation is null)
        {
            throw new HubException("Conversation not found for this user.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName.ForSalon(conversationId), Context.ConnectionAborted);
    }
}

/// <summary>
/// Canonical SignalR group names for the conversation hub. Kept in one place so the hub and
/// the broadcaster can never drift apart.
/// </summary>
public static class GroupName
{
    /// <summary>Group receiving all notifications for a single Aveline user.</summary>
    public static string ForUser(Guid userId) => $"user:{userId}";

    /// <summary>Group receiving all notifications broadcast to an organization.</summary>
    public static string ForOrganization(Guid organizationId) => $"org:{organizationId}";

    /// <summary>Group receiving messages for a single conversation (Salon).</summary>
    public static string ForSalon(Guid conversationId) => $"salon:{conversationId}";
}
