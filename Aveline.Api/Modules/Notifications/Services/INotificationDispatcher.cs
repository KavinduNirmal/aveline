using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Services;

/// <summary>
/// Orchestrates notification delivery: resolve recipients, route to eligible channels,
/// dispatch best-effort, and persist the notification and its delivery attempts.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Dispatches the notification, or does nothing when no recipient resolves.
    /// </summary>
    /// <returns>
    /// The <see cref="NotificationRecord"/> that was written, or <c>null</c> when no
    /// recipient resolved. Callers that need the record id use it; the two producers
    /// that only deliver await and ignore it.
    /// </returns>
    Task<NotificationRecord?> DispatchAsync(Notification notification, CancellationToken cancellationToken = default);
}
