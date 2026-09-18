using Aveline.Api.Modules.Notifications.Models;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Notifications.Channels;

/// <summary>
/// Demo/no-op push channel: logs that a push would be dispatched. The real FCM adapter
/// lands in a later slice; this keeps the dispatcher fully testable without credentials.
/// </summary>
public sealed class LoggingPushChannel : IPushChannel
{
    private readonly ILogger<LoggingPushChannel> _logger;

    public LoggingPushChannel(ILogger<LoggingPushChannel> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(ResolvedRecipient recipient, Notification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Push notification (demo delivery, provider not configured). User={UserId} Type={Type} Title={Title}",
            recipient.UserId, notification.Type, notification.Title);
        return Task.CompletedTask;
    }
}
