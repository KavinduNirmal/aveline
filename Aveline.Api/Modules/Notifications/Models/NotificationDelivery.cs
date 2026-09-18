namespace Aveline.Api.Modules.Notifications.Models;

/// <summary>
/// A single delivery attempt of a <see cref="NotificationRecord"/> to one user over one
/// channel. One row is written per (notification, recipient, channel) attempt so the
/// outcome of each channel is auditable independently.
/// </summary>
public class NotificationDelivery
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid NotificationRecordId { get; set; }

    public Guid UserId { get; set; }

    public NotificationChannel Channel { get; set; }

    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;

    public string? ErrorMessage { get; set; }

    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

    public NotificationRecord? Notification { get; set; }
}
