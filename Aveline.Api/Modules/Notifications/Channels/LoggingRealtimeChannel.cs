using Aveline.Api.Modules.Notifications.Models;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Notifications.Channels;

/// <summary>
/// Demo/no-op realtime channel: logs that a realtime message would be dispatched. The
/// SignalR-backed implementation lands in a later slice.
/// </summary>
public sealed class LoggingRealtimeChannel : IRealtimeChannel
{
    private readonly ILogger<LoggingRealtimeChannel> _logger;

    public LoggingRealtimeChannel(ILogger<LoggingRealtimeChannel> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(ResolvedRecipient recipient, Notification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Realtime notification (demo delivery). User={UserId} Type={Type} Title={Title}",
            recipient.UserId, notification.Type, notification.Title);
        return Task.CompletedTask;
    }
}
