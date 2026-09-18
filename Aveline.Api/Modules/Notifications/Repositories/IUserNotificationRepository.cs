using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Repositories;

public interface IUserNotificationRepository
{
    Task<UserNotification> AddAsync(UserNotification item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a page of the user's visible (non-dismissed) inbox items, newest first,
    /// with the underlying <see cref="NotificationRecord"/> populated.
    /// </summary>
    Task<(IReadOnlyList<UserNotification> Items, int Total)> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        bool unreadOnly,
        CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Returns a single visible inbox item scoped to the user, or null.</summary>
    Task<UserNotification?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    Task MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Marks all visible unread items as read; returns the number updated.</summary>
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);

    Task DismissAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    Task SetDeliveredAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}
