using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Tests for the event-reminder domain service (Issue #170): due events are dispatched and marked
/// as reminded; already-reminded, inactive, past or out-of-horizon events are skipped (dedupe).
/// Dispatch is asserted through a stub <see cref="INotificationDispatcher"/> (no real channel).
/// </summary>
public class EventReminderServiceTests
{
    private readonly AppDbContext _context;
    private readonly Guid _orgA = Guid.NewGuid();

    public EventReminderServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"EventReminder_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
    }

    private sealed class FakeDispatcher : INotificationDispatcher
    {
        public List<Notification> Sent { get; } = [];

        public Task DispatchAsync(Notification notification, CancellationToken cancellationToken = default)
        {
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    private async Task<Customer> EnsureCustomerAsync()
    {
        var customer = await _context.Customers.SingleOrDefaultAsync(c => c.OrganizationId == _orgA);
        if (customer is not null)
        {
            return customer;
        }

        var created = await _context.Customers.AddAsync(new Customer
        {
            OrganizationId = _orgA,
            PhoneNumber = "+94771234567",
            FullName = "Sarah Perera",
            Status = "vip",
        });
        await _context.SaveChangesAsync();
        return created.Entity;
    }

    private async Task<CustomerEvent> AddEventAsync(
        DateTime eventDate,
        DateTime? remindedAt = null,
        bool isActive = true)
    {
        var customer = await EnsureCustomerAsync();
        var evt = await _context.CustomerEvents.AddAsync(new CustomerEvent
        {
            OrganizationId = _orgA,
            CustomerId = customer.Id,
            EventType = "wedding",
            EventDate = eventDate,
            Description = "Daughter's wedding",
            IsActive = isActive,
            ReminderSentAt = remindedAt,
        });
        await _context.SaveChangesAsync();
        return evt.Entity;
    }

    private EventReminderService Sut(FakeDispatcher dispatcher)
        => new(new CustomerEventRepository(_context), dispatcher, NullLogger<EventReminderService>.Instance);

    [Fact]
    public async Task ProcessDue_DispatchesAndMarksDueEvent()
    {
        var evt = await AddEventAsync(DateTime.UtcNow.AddDays(1));
        var dispatcher = new FakeDispatcher();

        var sent = await Sut(dispatcher).ProcessDueEventsAsync();

        Assert.Equal(1, sent);
        var notification = Assert.Single(dispatcher.Sent);
        Assert.Equal(NotificationType.EventReminder, notification.Type);
        Assert.Equal(_orgA, notification.Target.OrganizationId);

        var stored = await _context.CustomerEvents.SingleAsync(e => e.Id == evt.Id);
        Assert.NotNull(stored.ReminderSentAt);
    }

    [Fact]
    public async Task ProcessDue_AlreadyReminded_IsNotRedispatched()
    {
        await AddEventAsync(DateTime.UtcNow.AddDays(1), remindedAt: DateTime.UtcNow);
        var dispatcher = new FakeDispatcher();

        var sent = await Sut(dispatcher).ProcessDueEventsAsync();

        Assert.Equal(0, sent);
        Assert.Empty(dispatcher.Sent);
    }

    [Fact]
    public async Task ProcessDue_OutOfHorizon_IsNotDispatched()
    {
        await AddEventAsync(DateTime.UtcNow.AddDays(60)); // beyond 30-day horizon
        var dispatcher = new FakeDispatcher();

        var sent = await Sut(dispatcher).ProcessDueEventsAsync();

        Assert.Equal(0, sent);
        Assert.Empty(dispatcher.Sent);
    }

    [Fact]
    public async Task ProcessDue_InactiveEvent_IsNotDispatched()
    {
        await AddEventAsync(DateTime.UtcNow.AddDays(1), isActive: false);
        var dispatcher = new FakeDispatcher();

        var sent = await Sut(dispatcher).ProcessDueEventsAsync();

        Assert.Equal(0, sent);
        Assert.Empty(dispatcher.Sent);
    }
}
