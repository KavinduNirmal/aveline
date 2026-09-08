namespace Aveline.Api.Modules.Notifications.Models;

/// <summary>
/// The catalog of notification types Aveline can emit. Extensible: adding a new
/// type is a new enum value plus a recipient-resolver rule — no delivery code changes.
/// </summary>
public enum NotificationType
{
    /// <summary>A customer sends a WhatsApp message.</summary>
    NewMessage,

    /// <summary>The Commerce Agent pauses for human-in-the-loop approval.</summary>
    ApprovalNeeded,

    /// <summary>A customer payment is confirmed.</summary>
    PaymentConfirmed,

    /// <summary>A scheduled check flags a VIP customer as at-risk.</summary>
    VipAtRisk,

    /// <summary>A scheduled reminder for an upcoming event.</summary>
    EventReminder,

    /// <summary>The Visual Agent finds a new customer-to-item match.</summary>
    NewMatch,
}
