using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Channels;

/// <summary>
/// Real-time delivery channel (SignalR) to connected web/mobile clients. Implementations
/// must be best-effort: a failure should be surfaced to the caller (the dispatcher logs
/// and records it) but must not take down other channels.
/// </summary>
public interface IRealtimeChannel
{
    /// <param name="inboxItemId">The recipient's inbox row, carried so a tap can mark it read.</param>
    /// <param name="unreadCount">
    /// The recipient's unread count after the row was written, so the client's badge can move
    /// without a full inbox read.
    /// </param>
    Task SendAsync(
        ResolvedRecipient recipient,
        Notification notification,
        Guid inboxItemId,
        int unreadCount,
        CancellationToken cancellationToken = default);
}
