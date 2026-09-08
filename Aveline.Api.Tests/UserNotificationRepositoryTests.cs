using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

public class UserNotificationRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly UserNotificationRepository _sut;

    public UserNotificationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"UserNotificationRepositoryTests_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _sut = new UserNotificationRepository(_context);
    }

    private async Task<NotificationRecord> SeedRecordAsync(Guid orgId, string title)
    {
        var record = new NotificationRecord
        {
            OrganizationId = orgId,
            Type = NotificationType.NewMessage,
            Title = title,
            Body = "Body",
            DataJson = "{}",
        };
        _context.NotificationRecords.Add(record);
        await _context.SaveChangesAsync();
        return record;
    }

    private async Task<UserNotification> SeedItemAsync(Guid userId, NotificationRecord record, bool read = false, bool dismissed = false)
    {
        var item = new UserNotification
        {
            UserId = userId,
            NotificationRecordId = record.Id,
            ReadAt = read ? DateTime.UtcNow : null,
            DismissedAt = dismissed ? DateTime.UtcNow : null,
        };
        _context.UserNotifications.Add(item);
        await _context.SaveChangesAsync();
        return item;
    }

    [Fact]
    public async Task AddAsync_PersistsItem_WithGeneratedId()
    {
        var record = await SeedRecordAsync(Guid.NewGuid(), "Hi");
        var item = new UserNotification { UserId = Guid.NewGuid(), NotificationRecordId = record.Id };

        var created = await _sut.AddAsync(item);

        Assert.NotEqual(default, created.Id);
        Assert.NotEqual(default, created.CreatedAt);
        Assert.Equal(1, await _context.UserNotifications.CountAsync());
    }

    [Fact]
    public async Task ListAsync_ReturnsVisibleItemsNewestFirst_ExcludingDismissed()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var r1 = await SeedRecordAsync(orgId, "First");
        var r2 = await SeedRecordAsync(orgId, "Second");
        await SeedItemAsync(userId, r1);
        await SeedItemAsync(userId, r2, dismissed: true);

        var (items, total) = await _sut.ListAsync(userId, 1, 50, unreadOnly: false);

        Assert.Equal(1, total);
        var item = Assert.Single(items);
        Assert.Equal("First", item.Notification!.Title);
    }

    [Fact]
    public async Task ListAsync_UnreadOnly_FiltersReadItems()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var r1 = await SeedRecordAsync(orgId, "Unread");
        var r2 = await SeedRecordAsync(orgId, "Read");
        await SeedItemAsync(userId, r1);
        await SeedItemAsync(userId, r2, read: true);

        var (items, total) = await _sut.ListAsync(userId, 1, 50, unreadOnly: true);

        Assert.Equal(1, total);
        Assert.Equal("Unread", Assert.Single(items).Notification!.Title);
    }

    [Fact]
    public async Task GetUnreadCountAsync_CountsOnlyVisibleUnread()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var r1 = await SeedRecordAsync(orgId, "A");
        var r2 = await SeedRecordAsync(orgId, "B");
        var r3 = await SeedRecordAsync(orgId, "C");
        await SeedItemAsync(userId, r1);
        await SeedItemAsync(userId, r2, read: true);
        await SeedItemAsync(userId, r3, dismissed: true);

        Assert.Equal(1, await _sut.GetUnreadCountAsync(userId));
    }

    [Fact]
    public async Task MarkReadAsync_SetsReadAt_OnlyForMatchingUser()
    {
        var userId = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var record = await SeedRecordAsync(orgId, "X");
        var item = await SeedItemAsync(userId, record);

        await _sut.MarkReadAsync(item.Id, otherUser);
        Assert.Null((await _context.UserNotifications.SingleAsync()).ReadAt);

        await _sut.MarkReadAsync(item.Id, userId);
        Assert.NotNull((await _context.UserNotifications.SingleAsync()).ReadAt);
    }

    [Fact]
    public async Task MarkAllReadAsync_MarksAllVisibleUnread_ReturnsCount()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var r1 = await SeedRecordAsync(orgId, "A");
        var r2 = await SeedRecordAsync(orgId, "B");
        var r3 = await SeedRecordAsync(orgId, "C");
        await SeedItemAsync(userId, r1);
        await SeedItemAsync(userId, r2);
        await SeedItemAsync(userId, r3, dismissed: true);

        var updated = await _sut.MarkAllReadAsync(userId);

        Assert.Equal(2, updated);
        Assert.Equal(0, await _sut.GetUnreadCountAsync(userId));
    }

    [Fact]
    public async Task DismissAsync_SetsDismissedAt()
    {
        var userId = Guid.NewGuid();
        var record = await SeedRecordAsync(Guid.NewGuid(), "Y");
        var item = await SeedItemAsync(userId, record);

        await _sut.DismissAsync(item.Id, userId);

        Assert.NotNull((await _context.UserNotifications.SingleAsync()).DismissedAt);
    }

    [Fact]
    public async Task SetDeliveredAsync_SetsDeliveredAt_OnlyOnce()
    {
        var userId = Guid.NewGuid();
        var record = await SeedRecordAsync(Guid.NewGuid(), "Z");
        var item = await SeedItemAsync(userId, record);

        await _sut.SetDeliveredAsync(item.Id, userId);
        var first = (await _context.UserNotifications.SingleAsync()).DeliveredAt;
        await _sut.SetDeliveredAsync(item.Id, userId);
        var second = (await _context.UserNotifications.SingleAsync()).DeliveredAt;

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }
}
