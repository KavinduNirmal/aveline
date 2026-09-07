namespace Aveline.Api.Modules.Notifications.Models;

/// <summary>
/// Persisted record of a dispatched notification (audit trail / future inbox). The
/// domain <see cref="Notification"/> is the transient dispatch contract; this entity
/// stores what was actually sent.
/// </summary>
public class NotificationRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The boutique organization this notification belongs to (tenant scope).</summary>
    public Guid OrganizationId { get; set; }

    public NotificationType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>JSON-serialized <see cref="Notification.Data"/> payload.</summary>
    public string DataJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<NotificationDelivery> Deliveries { get; set; } = [];
}
