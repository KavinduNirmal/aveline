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
}
