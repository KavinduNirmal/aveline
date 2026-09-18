namespace Aveline.Api.Modules.Notifications.Models;

/// <summary>
/// The client-facing payload delivered over the realtime channel. This is the stable
/// contract the React (#92) and Flutter (#93) clients consume via the
/// <c>ReceiveNotification</c> SignalR method. It deliberately omits the server-side
/// <see cref="NotificationTarget"/> intent.
/// </summary>
/// <param name="Type">The notification type (see <see cref="NotificationType"/>).</param>
/// <param name="Title">Short human-readable title.</param>
/// <param name="Body">Human-readable body text.</param>
/// <param name="Data">Type-specific structured payload (e.g. order id, approval id).</param>
public sealed record NotificationDto(
    NotificationType Type,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string?> Data);
