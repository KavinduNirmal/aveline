using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Channels;

/// <summary>
/// Push channel that sends a notification to every active device token of a recipient via
/// FCM. A failure is allowed to propagate so the dispatcher records the delivery as failed
/// (best-effort at the dispatcher level).
/// </summary>
public sealed class FcmPushChannel : IPushChannel
{
    private readonly IFirebaseMessagingClient _messaging;

    public FcmPushChannel(IFirebaseMessagingClient messaging)
    {
        _messaging = messaging;
    }

    public async Task SendAsync(
        ResolvedRecipient recipient,
        Notification notification,
        Guid inboxItemId,
        CancellationToken cancellationToken = default)
    {
        // The notification's own payload plus the two keys a closed app needs: the
        // kind, so it renders the right tile, and the inbox row, so a tap can mark
        // that notification read. Every value is a string and the sender drops
        // nulls, so a consumer treats each key as optional.
        var data = notification.Data.ToDictionary(kv => kv.Key, kv => kv.Value);
        data["type"] = notification.Type.ToString();
        data["notificationId"] = inboxItemId.ToString();

        foreach (var token in recipient.DeviceTokens)
        {
            await _messaging.SendAsync(token, notification.Title, notification.Body, data, cancellationToken);
        }
    }
}
