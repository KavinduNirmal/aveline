namespace Aveline.Api.Modules.Notifications.Models;

/// <summary>
/// Delivery channels a notification may be sent over. Flags so a single
/// notification can target multiple channels.
/// </summary>
[Flags]
public enum NotificationChannel
{
    /// <summary>No channel selected.</summary>
    None = 0,

    /// <summary>Real-time delivery (SignalR) to connected web/mobile clients.</summary>
    Realtime = 1,

    /// <summary>Mobile push delivery (FCM).</summary>
    Push = 2,

    /// <summary>Email delivery.</summary>
    Email = 4,
}
