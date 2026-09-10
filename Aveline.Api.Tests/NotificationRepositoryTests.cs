using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

public class NotificationRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly NotificationRepository _sut;

    public NotificationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"NotificationRepositoryTests_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _sut = new NotificationRepository(_context);
    }

    [Fact]
    public async Task AddAsync_PersistsRecord_WithGeneratedId()
    {
        var record = new NotificationRecord
        {
            OrganizationId = Guid.NewGuid(),
            Type = NotificationType.NewMessage,
            Title = "New message",
            Body = "A customer messaged",
            DataJson = "{}",
        };

        var created = await _sut.AddAsync(record);

        Assert.NotEqual(default, created.Id);
        Assert.NotEqual(default, created.CreatedAt);
        Assert.Equal(1, await _context.NotificationRecords.CountAsync());
    }

    [Fact]
    public async Task AddDeliveryAsync_PersistsDelivery_LinkedToRecord()
    {
        var record = await _sut.AddAsync(new NotificationRecord
        {
            OrganizationId = Guid.NewGuid(),
            Type = NotificationType.EventReminder,
            Title = "Reminder",
            Body = "Event soon",
            DataJson = "{}",
        });

        var delivery = new NotificationDelivery
        {
            NotificationRecordId = record.Id,
            UserId = Guid.NewGuid(),
            Channel = NotificationChannel.Push,
            Status = DeliveryStatus.Delivered,
        };

        await _sut.AddDeliveryAsync(delivery);

        var stored = await _context.NotificationDeliveries.SingleAsync();
        Assert.Equal(record.Id, stored.NotificationRecordId);
        Assert.Equal(NotificationChannel.Push, stored.Channel);
        Assert.Equal(DeliveryStatus.Delivered, stored.Status);
    }
}
