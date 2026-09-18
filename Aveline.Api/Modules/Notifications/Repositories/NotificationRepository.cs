using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly AppDbContext _context;

    public NotificationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<NotificationRecord> AddAsync(NotificationRecord record, CancellationToken cancellationToken = default)
    {
        record.CreatedAt = DateTime.UtcNow;
        _context.NotificationRecords.Add(record);
        await _context.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task AddDeliveryAsync(NotificationDelivery delivery, CancellationToken cancellationToken = default)
    {
        delivery.AttemptedAt = DateTime.UtcNow;
        _context.NotificationDeliveries.Add(delivery);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
