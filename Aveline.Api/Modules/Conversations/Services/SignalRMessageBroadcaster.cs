using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// Sends <c>ReceiveMessage</c> to the <c>salon:&#123;conversationId&#125;</c> group via SignalR.
/// </summary>
public class SignalRMessageBroadcaster : IMessageBroadcaster
{
    private readonly IHubContext<ConversationHub> _hubContext;

    public SignalRMessageBroadcaster(IHubContext<ConversationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task BroadcastMessageAsync(MessageDto message, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group(GroupName.ForSalon(message.ConversationId))
            .SendAsync("ReceiveMessage", message, cancellationToken);
    }

    public async Task BroadcastAgentStateAsync(AgentStateDto state, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group(GroupName.ForSalon(state.ConversationId))
            .SendAsync("ReceiveAgentState", state, cancellationToken);
    }
}
