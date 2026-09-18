namespace Aveline.Api.Modules.Notifications.Models;

/// <summary>
/// Delivery lifecycle state of a single (notification, recipient, channel) attempt.
/// </summary>
public enum DeliveryStatus
{
    /// <summary>Dispatch has not completed.</summary>
    Pending,

    /// <summary>The channel accepted the notification.</summary>
    Delivered,

    /// <summary>The channel reported a failure.</summary>
    Failed,
}
