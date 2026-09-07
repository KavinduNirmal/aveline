using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.Notifications.Services;

/// <summary>
/// Default channel router. Realtime is always eligible when requested. Push is eligible
/// only when the user has opted in <em>and</em> has at least one registered device token.
/// Email is eligible only when the user's contact preference is email.
/// </summary>
public sealed class ChannelRouter : IChannelRouter
{
    public NotificationChannel AllowedChannels(ResolvedRecipient recipient, NotificationChannel requested)
    {
        var eligible = NotificationChannel.Realtime;

        if (recipient.PushEnabled && recipient.DeviceTokens.Count > 0)
        {
            eligible |= NotificationChannel.Push;
        }

        if (recipient.ContactPreference == ContactPreferences.Email)
        {
            eligible |= NotificationChannel.Email;
        }

        return requested & eligible;
    }
}
