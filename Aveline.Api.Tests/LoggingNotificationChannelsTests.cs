using Aveline.Api.Modules.Notifications.Channels;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Tests;

public class LoggingNotificationChannelsTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private static ResolvedRecipient Recipient() => new(
        Guid.NewGuid(),
        "user@aveline.lk",
        true,
        ContactPreferences.Email,
        ["tok"]);

    private static Notification NotificationFor() => new(
        NotificationType.NewMessage,
        "New message",
        "A customer messaged",
        new NotificationTarget(Guid.NewGuid()),
        new Dictionary<string, string?> { ["secret"] = "do-not-log" },
        NotificationChannel.Realtime);

    [Fact]
    public async Task LoggingRealtimeChannel_RecordsDispatch_WithoutDataPayload()
    {
        var logger = new CapturingLogger<LoggingRealtimeChannel>();
        var channel = new LoggingRealtimeChannel(logger);

        await channel.SendAsync(Recipient(), NotificationFor());

        var log = Assert.Single(logger.Messages);
        Assert.Contains("New message", log);
        Assert.DoesNotContain("do-not-log", log);
    }

    [Fact]
    public async Task LoggingPushChannel_RecordsDispatch_WithoutDataPayload()
    {
        var logger = new CapturingLogger<LoggingPushChannel>();
        var channel = new LoggingPushChannel(logger);

        await channel.SendAsync(Recipient(), NotificationFor());

        var log = Assert.Single(logger.Messages);
        Assert.Contains("New message", log);
        Assert.DoesNotContain("do-not-log", log);
    }

    [Fact]
    public async Task LoggingEmailChannel_RecordsDispatch_WithoutDataPayload()
    {
        var logger = new CapturingLogger<LoggingEmailChannel>();
        var channel = new LoggingEmailChannel(logger);

        await channel.SendAsync(Recipient(), NotificationFor());

        var log = Assert.Single(logger.Messages);
        Assert.Contains("user@aveline.lk", log);
        Assert.DoesNotContain("do-not-log", log);
    }
}
