using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Repositories;

public interface INotificationRepository
{
    Task<NotificationRecord> AddAsync(NotificationRecord record, CancellationToken cancellationToken = default);

    Task AddDeliveryAsync(NotificationDelivery delivery, CancellationToken cancellationToken = default);
}
