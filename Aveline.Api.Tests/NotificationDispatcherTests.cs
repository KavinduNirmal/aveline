using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Notifications.Channels;
using Aveline.Api.Modules.Notifications.Metrics;
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

    /// <summary>One recorded send, with whatever the channel was handed.</summary>
    private sealed record ChannelCall(Guid UserId, Notification Notification, Guid? InboxItemId, int? UnreadCount);

    private sealed class ChannelRecorder
    {
        public List<ChannelCall> Calls { get; } = [];
        public Exception? ThrowOnSend { get; set; }

        public Task Send(
            ResolvedRecipient recipient,
            Notification notification,
            Guid? inboxItemId,
            int? unreadCount,
            CancellationToken cancellationToken)
        {
            Calls.Add(new ChannelCall(recipient.UserId, notification, inboxItemId, unreadCount));
            return ThrowOnSend is not null ? Task.FromException(ThrowOnSend) : Task.CompletedTask;
        }
    }

    // Each channel is its own type on purpose. One recorder implementing all three
    // interfaces would always match the dispatcher's first switch arm, so the push
    // and email signatures would never be exercised.
    private sealed class RecordingRealtimeChannel(ChannelRecorder recorder) : IRealtimeChannel
    {
        public Task SendAsync(ResolvedRecipient recipient, Notification notification, Guid inboxItemId, int unreadCount, CancellationToken cancellationToken = default)
            => recorder.Send(recipient, notification, inboxItemId, unreadCount, cancellationToken);
    }

    private sealed class RecordingPushChannel(ChannelRecorder recorder) : IPushChannel
    {
        public Task SendAsync(ResolvedRecipient recipient, Notification notification, Guid inboxItemId, CancellationToken cancellationToken = default)
            => recorder.Send(recipient, notification, inboxItemId, null, cancellationToken);
    }

    private sealed class RecordingEmailChannel(ChannelRecorder recorder) : IEmailChannel
    {
        public Task SendAsync(ResolvedRecipient recipient, Notification notification, CancellationToken cancellationToken = default)
            => recorder.Send(recipient, notification, null, null, cancellationToken);
    }

    private sealed class Channels
    {
        public Channels()
        {
            Realtime = new RecordingRealtimeChannel(Recorder);
            Push = new RecordingPushChannel(Recorder);
            Email = new RecordingEmailChannel(Recorder);
        }

        public ChannelRecorder Recorder { get; } = new();
        public RecordingRealtimeChannel Realtime { get; }
        public RecordingPushChannel Push { get; }
        public RecordingEmailChannel Email { get; }

        public IReadOnlyList<ChannelCall> Calls => Recorder.Calls;

        /// <summary>Realtime sends are the ones that carry a count.</summary>
        public IReadOnlyList<ChannelCall> RealtimeCalls =>
            [.. Recorder.Calls.Where(call => call.InboxItemId is not null && call.UnreadCount is not null)];

        /// <summary>Push sends carry the inbox row id and no count.</summary>
        public IReadOnlyList<ChannelCall> PushCalls =>
            [.. Recorder.Calls.Where(call => call.InboxItemId is not null && call.UnreadCount is null)];

        /// <summary>Email sends carry neither.</summary>
        public IReadOnlyList<ChannelCall> EmailCalls =>
            [.. Recorder.Calls.Where(call => call.InboxItemId is null)];

        public Exception? ThrowOnSend
        {
            set => Recorder.ThrowOnSend = value;
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
        Channels channels)
    {
        return new NotificationDispatcher(
            resolver,
            router,
            _repository,
            _inbox,
            channels.Push,
            channels.Realtime,
            channels.Email,
            _logger);
    }

    [Fact]
    public async Task DispatchAsync_ResolvesAndSendsToAllowedChannels_AndPersistsDeliveries()
    {
        var orgId = Guid.NewGuid();
        var recipient = Recipient();
        var channels = new Channels();
        var dispatcher = BuildDispatcher(
            new FakeResolver([recipient]),
            new FakeRouter(NotificationChannel.Realtime | NotificationChannel.Push | NotificationChannel.Email),
            channels);

        await dispatcher.DispatchAsync(NotificationFor(orgId));

        // All three channels invoked once for the single recipient.
        Assert.Equal(3, channels.Calls.Count);
        Assert.All(channels.Calls, c => Assert.Equal(recipient.UserId, c.UserId));

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
        var channels = new Channels { ThrowOnSend = new InvalidOperationException("boom") };
        var dispatcher = BuildDispatcher(
            new FakeResolver([recipient]),
            new FakeRouter(NotificationChannel.Realtime | NotificationChannel.Push | NotificationChannel.Email),
            channels);

        await dispatcher.DispatchAsync(NotificationFor(orgId));

        // All three channels were attempted (each threw), but no exception escaped.
        Assert.Equal(3, channels.Calls.Count);

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
        var channels = new Channels();
        var dispatcher = BuildDispatcher(
            new FakeResolver([]),
            new FakeRouter(NotificationChannel.Realtime | NotificationChannel.Push | NotificationChannel.Email),
            channels);

        await dispatcher.DispatchAsync(NotificationFor(orgId));

        Assert.Empty(channels.Calls);
        Assert.False(await _context.NotificationRecords.AnyAsync());
        Assert.False(await _context.NotificationDeliveries.AnyAsync());
        Assert.False(await _context.UserNotifications.AnyAsync());
    }

    [Fact]
    public async Task DispatchAsync_OnlyRoutesAllowedChannels()
    {
        var orgId = Guid.NewGuid();
        var recipient = Recipient();
        var channels = new Channels();
        var dispatcher = BuildDispatcher(
            new FakeResolver([recipient]),
            new FakeRouter(NotificationChannel.Push),
            channels);

        await dispatcher.DispatchAsync(NotificationFor(orgId));

        // Only the Push channel is routed (router returned Push only).
        Assert.Single(channels.PushCalls);
        Assert.Empty(channels.RealtimeCalls);
        Assert.Empty(channels.EmailCalls);
        var deliveries = await _context.NotificationDeliveries.ToListAsync();
        var delivery = Assert.Single(deliveries);
        Assert.Equal(NotificationChannel.Push, delivery.Channel);
    }

    [Fact]
    public async Task DispatchAsync_PassesTheInboxRowIdAndTheRecipientsCountToTheChannels()
    {
        var orgId = Guid.NewGuid();
        var recipient = Recipient();
        var channels = new Channels();
        var dispatcher = BuildDispatcher(
            new FakeResolver([recipient]),
            new FakeRouter(NotificationChannel.Realtime | NotificationChannel.Push | NotificationChannel.Email),
            channels);

        await dispatcher.DispatchAsync(NotificationFor(orgId));

        var inbox = await _context.UserNotifications.SingleAsync();

        // Realtime carries the row id and the count computed after the row was
        // written, which is one unread for a fresh single-recipient dispatch.
        var realtime = Assert.Single(channels.RealtimeCalls);
        Assert.Equal(inbox.Id, realtime.InboxItemId);
        Assert.Equal(1, realtime.UnreadCount);

        // Push carries the row id (S4 merges it into the FCM data map).
        var push = Assert.Single(channels.PushCalls);
        Assert.Equal(inbox.Id, push.InboxItemId);
        Assert.Null(push.UnreadCount);

        // Email carries nothing new.
        var email = Assert.Single(channels.EmailCalls);
        Assert.Null(email.InboxItemId);
        Assert.Null(email.UnreadCount);
    }

    [Fact]
    public async Task DispatchAsync_TwoRecipients_CountIsPerRecipient()
    {
        var orgId = Guid.NewGuid();
        var first = Recipient();
        var second = Recipient();
        var channels = new Channels();
        var dispatcher = BuildDispatcher(
            new FakeResolver([first, second]),
            new FakeRouter(NotificationChannel.Realtime),
            channels);

        await dispatcher.DispatchAsync(NotificationFor(orgId));

        // Each recipient's count reflects only their own inbox.
        Assert.Equal(2, channels.RealtimeCalls.Count);
        Assert.All(channels.RealtimeCalls, call => Assert.Equal(1, call.UnreadCount));
    }

    /// <summary>
    /// Slice 7 constraint 2: the delivery counter is incremented in the dispatcher, not derived at
    /// scrape time. One attempt per (recipient, channel) — the same rows the repository persists.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_IncrementsTheDeliveryCounterPerAttempt()
    {
        var recorded = new List<string>();
        using var metrics = new NotificationMetrics(fcmCredentialConfigured: true);
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, probe) =>
        {
            if (instrument.Meter.Name == NotificationMetrics.MeterName
                && instrument.Name == "aveline.notification.delivery")
            {
                probe.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? channel = null;
            string? status = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "channel")
                {
                    channel = tag.Value?.ToString();
                }
                else if (tag.Key == "status")
                {
                    status = tag.Value?.ToString();
                }
            }

            lock (recorded)
            {
                recorded.Add($"{channel}:{status}:{value}");
            }
        });
        listener.Start();

        var channels = new Channels();
        var dispatcher = new NotificationDispatcher(
            new FakeResolver([Recipient()]),
            new FakeRouter(NotificationChannel.Realtime | NotificationChannel.Push | NotificationChannel.Email),
            _repository,
            _inbox,
            channels.Push,
            channels.Realtime,
            channels.Email,
            _logger,
            metrics);

        await dispatcher.DispatchAsync(NotificationFor(Guid.NewGuid()));

        recorded.Should().HaveCount(3);
        recorded.Should().OnlyContain(entry => entry.EndsWith(":1", StringComparison.Ordinal));
        recorded.Should().Contain(entry => entry.StartsWith("Realtime:Delivered", StringComparison.Ordinal));
    }
}
