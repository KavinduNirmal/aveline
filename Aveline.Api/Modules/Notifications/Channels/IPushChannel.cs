using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Channels;

/// <summary>
/// Mobile push delivery channel (FCM). Implementations must be best-effort: a failure
/// should be surfaced to the caller (the dispatcher logs and records it) but must not
/// take down other channels.
/// </summary>
public interface IPushChannel
{
    /// <param name="inboxItemId">
    /// The recipient's inbox row, merged into the push data so a tap can mark that
    /// notification read.
    /// </param>
    Task SendAsync(
        ResolvedRecipient recipient,
        Notification notification,
        Guid inboxItemId,
        CancellationToken cancellationToken = default);
}
