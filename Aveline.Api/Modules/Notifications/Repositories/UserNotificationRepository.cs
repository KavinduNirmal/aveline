using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Notifications.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Notifications.Repositories;

public class UserNotificationRepository : IUserNotificationRepository
{
    private readonly AppDbContext _context;

    public UserNotificationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<UserNotification> AddAsync(UserNotification item, CancellationToken cancellationToken = default)
    {
        item.CreatedAt = DateTime.UtcNow;
        _context.UserNotifications.Add(item);
        await _context.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<(IReadOnlyList<UserNotification> Items, int Total)> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        bool unreadOnly,
        CancellationToken cancellationToken = default)
    {
        var query = _context.UserNotifications
            .AsNoTracking()
            .Include(n => n.Notification)
            .Where(n => n.UserId == userId && n.DismissedAt == null);

        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserNotifications
            .AsNoTracking()
            .CountAsync(
                n => n.UserId == userId && n.DismissedAt == null && n.ReadAt == null,
                cancellationToken);
    }

    public async Task<UserNotification?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserNotifications
            .AsNoTracking()
            .Include(n => n.Notification)
            .FirstOrDefaultAsync(
                n => n.Id == id && n.UserId == userId && n.DismissedAt == null,
                cancellationToken);
    }

    public async Task MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var item = await _context.UserNotifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId && n.DismissedAt == null, cancellationToken);

        if (item is not null && item.ReadAt is null)
        {
            item.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var items = await _context.UserNotifications
            .Where(n => n.UserId == userId && n.DismissedAt == null && n.ReadAt == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            item.ReadAt = now;
        }

        if (items.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return items.Count;
    }

    public async Task DismissAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var item = await _context.UserNotifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId && n.DismissedAt == null, cancellationToken);

        if (item is not null)
        {
            item.DismissedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SetDeliveredAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var item = await _context.UserNotifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);

        if (item is not null && item.DeliveredAt is null)
        {
            item.DeliveredAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
