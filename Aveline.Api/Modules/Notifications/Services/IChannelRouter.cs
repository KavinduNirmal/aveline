using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Services;

/// <summary>
/// Decides which channels a resolved recipient is actually eligible for, narrowing the
/// channels requested on the <see cref="Notification"/> to those the user can receive.
/// </summary>
public interface IChannelRouter
{
    NotificationChannel AllowedChannels(ResolvedRecipient recipient, NotificationChannel requested);
}
