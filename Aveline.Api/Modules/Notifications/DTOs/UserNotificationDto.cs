using System.Text.Json;
using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.DTOs;

/// <summary>
/// A single inbox item returned to a user. The <see cref="Data"/> payload is the parsed
/// <see cref="NotificationRecord.DataJson"/> of the underlying notification.
/// </summary>
/// <param name="OrganizationId">
/// The dispatching organisation. It is already required on the record, so this is a
/// projection rather than a new column; it lets a merged inbox label each row with the
/// boutique it belongs to.
/// </param>
/// <param name="OrganizationName">
/// The boutique's name, or <c>null</c> when the organisation could not be loaded.
/// </param>
public sealed record UserNotificationDto(
    Guid Id,
    Guid NotificationId,
    string Type,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string?> Data,
    bool IsRead,
    DateTime? ReadAt,
    DateTime? DeliveredAt,
    DateTime CreatedAt,
    Guid OrganizationId,
    string? OrganizationName)
{
    public static UserNotificationDto From(UserNotification item)
    {
        var notification = item.Notification;
        IReadOnlyDictionary<string, string?> data = new Dictionary<string, string?>();
        if (notification is not null && !string.IsNullOrWhiteSpace(notification.DataJson))
        {
            try
            {
                data = JsonSerializer.Deserialize<Dictionary<string, string?>>(notification.DataJson)
                       ?? new Dictionary<string, string?>();
            }
            catch (JsonException)
            {
                data = new Dictionary<string, string?>();
            }
        }

        return new UserNotificationDto(
            item.Id,
            item.NotificationRecordId,
            notification?.Type.ToString() ?? string.Empty,
            notification?.Title ?? string.Empty,
            notification?.Body ?? string.Empty,
            data,
            item.ReadAt is not null,
            item.ReadAt,
            item.DeliveredAt,
            item.CreatedAt,
            notification?.OrganizationId ?? Guid.Empty,
            notification?.Organization?.Name);
    }
}
