using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Services;

/// <summary>
/// Orchestrates notification delivery: resolve recipients, route to eligible channels,
/// dispatch best-effort, and persist the notification and its delivery attempts.
/// </summary>
public interface INotificationDispatcher
{
    Task DispatchAsync(Notification notification, CancellationToken cancellationToken = default);
}
