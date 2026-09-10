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

    public async Task SendAsync(ResolvedRecipient recipient, Notification notification, CancellationToken cancellationToken = default)
    {
        foreach (var token in recipient.DeviceTokens)
        {
            await _messaging.SendAsync(token, notification.Title, notification.Body, notification.Data, cancellationToken);
        }
    }
}
