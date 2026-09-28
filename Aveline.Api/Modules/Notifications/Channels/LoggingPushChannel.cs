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

    public Task SendAsync(
        ResolvedRecipient recipient,
        Notification notification,
        Guid inboxItemId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Push notification (demo delivery, provider not configured). User={UserId} Type={Type} Title={Title} InboxItemId={InboxItemId}",
            recipient.UserId, notification.Type, notification.Title, inboxItemId);
        return Task.CompletedTask;
    }
}
