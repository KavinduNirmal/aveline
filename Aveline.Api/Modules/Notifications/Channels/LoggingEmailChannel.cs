using Aveline.Api.Modules.Notifications.Models;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Notifications.Channels;

/// <summary>
/// Demo/no-op email channel: logs that an email would be dispatched. Delivery provider
/// integration is deferred; the recipient address is recorded but never the data payload.
/// </summary>
public sealed class LoggingEmailChannel : IEmailChannel
{
    private readonly ILogger<LoggingEmailChannel> _logger;

    public LoggingEmailChannel(ILogger<LoggingEmailChannel> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(ResolvedRecipient recipient, Notification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Email notification (demo delivery, provider not configured). To={Email} Type={Type} Title={Title}",
            recipient.Email, notification.Type, notification.Title);
        return Task.CompletedTask;
    }
}
