namespace Aveline.Api.Modules.Notifications.Models;

/// <summary>
/// A per-user inbox item linking a user to a dispatched <see cref="NotificationRecord"/>.
/// Carries the user's read/delivered/dismissed state. One row is created per recipient when
/// a notification is dispatched; the underlying <see cref="NotificationRecord"/> and
/// <see cref="NotificationDelivery"/> rows remain the audit trail.
/// </summary>
public class UserNotification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public Guid NotificationRecordId { get; set; }

    /// <summary>When the user read the notification; null while unread.</summary>
    public DateTime? ReadAt { get; set; }

    /// <summary>When at least one delivery channel succeeded; null if not yet delivered.</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>When the user dismissed the notification (soft delete); null while visible.</summary>
    public DateTime? DismissedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public NotificationRecord? Notification { get; set; }
}
