using Aveline.Api.Modules.Conversations.DTOs;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// Broadcasts a persisted message to the clients subscribed to its Salon group.
/// </summary>
public interface IMessageBroadcaster
{
    Task BroadcastMessageAsync(MessageDto message, CancellationToken cancellationToken = default);
}
