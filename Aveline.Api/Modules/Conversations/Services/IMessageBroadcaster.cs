using Aveline.Api.Modules.Conversations.DTOs;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// Broadcasts a persisted message to the clients subscribed to its Salon group.
/// </summary>
public interface IMessageBroadcaster
{
    Task BroadcastMessageAsync(MessageDto message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts Aveline's current agentic-workflow state to the Salon group so clients can
    /// animate the blossom avatar.
    /// </summary>
    Task BroadcastAgentStateAsync(AgentStateDto state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts a conversation's inbox tile as <c>ReceiveConversationChanged</c>, so an open
    /// inbox updates without a re-list.
    /// </summary>
    /// <remarks>
    /// The target group follows the routing rule in <see cref="ConversationTile"/>: an
    /// organization-shared thread (customer-bound or channel) goes to the org group, while a
    /// per-user general Salon goes only to its owner's user group.
    /// </remarks>
    Task BroadcastConversationChangedAsync(
        ConversationTile tile,
        CancellationToken cancellationToken = default);
}
