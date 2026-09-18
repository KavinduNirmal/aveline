using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Notifications.Channels;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Repositories;
using Aveline.Api.Modules.Notifications.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Tests;

public class NotificationDispatcherTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private sealed class FakeResolver(IReadOnlyList<ResolvedRecipient> recipients) : IRecipientResolver
    {
        public Task<IReadOnlyList<ResolvedRecipient>> ResolveAsync(Notification notification, CancellationToken ct = default)
            => Task.FromResult(recipients);
    }

    private sealed class FakeRouter(NotificationChannel allowed) : IChannelRouter
    {
        public NotificationChannel AllowedChannels(ResolvedRecipient recipient, NotificationChannel requested)
            => allowed & requested;
    }

    private sealed class RecordingChannel<T> : IPushChannel, IRealtimeChannel, IEmailChannel
    {
        private readonly Func<ResolvedRecipient, Notification, CancellationToken, Task> _send;
        public List<(Guid UserId, Notification Notification)> Calls { get; } = [];

        public RecordingChannel(Func<ResolvedRecipient, Notification, CancellationToken, Task>? send = null)
            => _send = send ?? ((_, _, _) => Task.CompletedTask);

        public Task SendAsync(ResolvedRecipient recipient, Notification notification, CancellationToken ct = default)
        {
            Calls.Add((recipient.UserId, notification));
            return _send(recipient, notification, ct);
        }
    }

    private readonly AppDbContext _context;
    private readonly NotificationRepository _repository;
    private readonly UserNotificationRepository _inbox;
    private readonly CapturingLogger<NotificationDispatcher> _logger;

    public NotificationDispatcherTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"DispatcherTests_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _repository = new NotificationRepository(_context);
        _inbox = new UserNotificationRepository(_context);
        _logger = new CapturingLogger<NotificationDispatcher>();
    }

    private static ResolvedRecipient Recipient(Guid? id = null) => new(
        id ?? Guid.NewGuid(),
        "user@aveline.lk",
        true,
        ContactPreferences.Email,
        ["tok"]);

    private static Notification NotificationFor(Guid orgId) => new(
        NotificationType.PaymentConfirmed,
        "Payment confirmed",
        "Order paid",
        new NotificationTarget(orgId),
        new Dictionary<string, string?> { ["orderId"] = "ord-1" },
        NotificationChannel.Realtime | NotificationChannel.Push | NotificationChannel.Email);

    private NotificationDispatcher BuildDispatcher(
        IRecipientResolver resolver,
        IChannelRouter router,
        RecordingChannel<object> channel)
    {
        return new NotificationDispatcher(
            resolver,
            router,
            _repository,
            _inbox,
            channel,
            channel,
            channel,
            _logger);
    }

    [Fact]
    public async Task DispatchAsync_ResolvesAndSendsToAllowedChannels_AndPersistsDeliveries()
    {
        var orgId = Guid.NewGuid();
        var recipient = Recipient();
        var channel = new RecordingChannel<object>();
        var dispatcher = BuildDispatcher(
            new FakeResolver([recipient]),
            new FakeRouter(NotificationChannel.Realtime | NotificationChannel.Push | NotificationChannel.Email),
            channel);

        await dispatcher.DispatchAsync(NotificationFor(orgId));

        // All three channels invoked once for the single recipient.
        Assert.Equal(3, channel.Calls.Count);
        Assert.All(channel.Calls, c => Assert.Equal(recipient.UserId, c.UserId));

        // One notification record persisted.
        var record = await _context.NotificationRecords.SingleAsync();
        Assert.Equal(orgId, record.OrganizationId);
        Assert.Equal(NotificationType.PaymentConfirmed, record.Type);
        Assert.Contains("ord-1", record.DataJson);

        // Three deliveries persisted, all Delivered.
        var deliveries = await _context.NotificationDeliveries.ToListAsync();
        Assert.Equal(3, deliveries.Count);
        Assert.All(deliveries, d => Assert.Equal(DeliveryStatus.Delivered, d.Status));

        // One inbox item created for the recipient and marked delivered.
        var inbox = await _context.UserNotifications.SingleAsync();
        Assert.Equal(recipient.UserId, inbox.UserId);
        Assert.Equal(record.Id, inbox.NotificationRecordId);
        Assert.NotNull(inbox.DeliveredAt);
        Assert.Null(inbox.ReadAt);
    }

    [Fact]
    public async Task DispatchAsync_OneChannelThrows_OthersStillDelivered_AndNoExceptionPropagates()
    {
        var orgId = Guid.NewGuid();
        var recipient = Recipient();
        var channel = new RecordingChannel<object>((_, _, _) => throw new InvalidOperationException("boom"));
        var dispatcher = BuildDispatcher(
            new FakeResolver([recipient]),
            new FakeRouter(NotificationChannel.Realtime | NotificationChannel.Push | NotificationChannel.Email),
            channel);

        await dispatcher.DispatchAsync(NotificationFor(orgId));

        // All three channels were attempted (each threw), but no exception escaped.
        Assert.Equal(3, channel.Calls.Count);

        var deliveries = await _context.NotificationDeliveries.ToListAsync();
        Assert.Equal(3, deliveries.Count);
        Assert.All(deliveries, d => Assert.Equal(DeliveryStatus.Failed, d.Status));
        Assert.All(deliveries, d => Assert.Contains("boom", d.ErrorMessage));

        // Inbox item is created but not marked delivered when every channel fails.
        var inbox = await _context.UserNotifications.SingleAsync();
        Assert.Null(inbox.DeliveredAt);
    }

    [Fact]
    public async Task DispatchAsync_NoRecipients_PersistsNothingAndSendsNothing()
    {
        var orgId = Guid.NewGuid();
        var channel = new RecordingChannel<object>();
        var dispatcher = BuildDispatcher(
            new FakeResolver([]),
            new FakeRouter(NotificationChannel.Realtime | NotificationChannel.Push | NotificationChannel.Email),
            channel);

        await dispatcher.DispatchAsync(NotificationFor(orgId));

        Assert.Empty(channel.Calls);
        Assert.False(await _context.NotificationRecords.AnyAsync());
        Assert.False(await _context.NotificationDeliveries.AnyAsync());
        Assert.False(await _context.UserNotifications.AnyAsync());
    }

    [Fact]
    public async Task DispatchAsync_OnlyRoutesAllowedChannels()
    {
        var orgId = Guid.NewGuid();
        var recipient = Recipient();
        var channel = new RecordingChannel<object>();
        var dispatcher = BuildDispatcher(
            new FakeResolver([recipient]),
            new FakeRouter(NotificationChannel.Push),
            channel);

        await dispatcher.DispatchAsync(NotificationFor(orgId));

        // Only the Push channel is routed (router returned Push only).
        Assert.Single(channel.Calls);
        var deliveries = await _context.NotificationDeliveries.ToListAsync();
        var delivery = Assert.Single(deliveries);
        Assert.Equal(NotificationChannel.Push, delivery.Channel);
    }
}
