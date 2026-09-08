using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Channels;

/// <summary>
/// Real-time delivery channel (SignalR) to connected web/mobile clients. Implementations
/// must be best-effort: a failure should be surfaced to the caller (the dispatcher logs
/// and records it) but must not take down other channels.
/// </summary>
public interface IRealtimeChannel
{
    Task SendAsync(ResolvedRecipient recipient, Notification notification, CancellationToken cancellationToken = default);
}
