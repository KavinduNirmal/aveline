using System.Text.Json;
using Aveline.Api.Modules.Notifications.Channels;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Repositories;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Notifications.Services;

/// <summary>
/// Default dispatcher. Resolves recipients, routes each to its eligible channels, sends
/// best-effort over each channel (a failure in one channel is logged and recorded but
/// never aborts the others), and persists the notification plus one delivery row per
/// (recipient, channel) attempt.
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly IRecipientResolver _resolver;
    private readonly IChannelRouter _router;
    private readonly INotificationRepository _repository;
    private readonly IPushChannel _push;
    private readonly IRealtimeChannel _realtime;
    private readonly IEmailChannel _email;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IRecipientResolver resolver,
        IChannelRouter router,
        INotificationRepository repository,
        IPushChannel push,
        IRealtimeChannel realtime,
        IEmailChannel email,
        ILogger<NotificationDispatcher> logger)
    {
        _resolver = resolver;
        _router = router;
        _repository = repository;
        _push = push;
        _realtime = realtime;
        _email = email;
        _logger = logger;
    }

    public async Task DispatchAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        var recipients = await _resolver.ResolveAsync(notification, cancellationToken);
        if (recipients.Count == 0)
        {
            _logger.LogInformation("Notification {Type} resolved to no recipients; nothing dispatched.", notification.Type);
            return;
        }

        var record = await _repository.AddAsync(new NotificationRecord
        {
            OrganizationId = notification.Target.OrganizationId,
            Type = notification.Type,
            Title = notification.Title,
            Body = notification.Body,
            DataJson = JsonSerializer.Serialize(notification.Data),
        }, cancellationToken);

        foreach (var recipient in recipients)
        {
            var allowed = _router.AllowedChannels(recipient, notification.Channels);

            await TrySendAsync(record, recipient, notification, allowed, NotificationChannel.Realtime, _realtime, cancellationToken);
            await TrySendAsync(record, recipient, notification, allowed, NotificationChannel.Push, _push, cancellationToken);
            await TrySendAsync(record, recipient, notification, allowed, NotificationChannel.Email, _email, cancellationToken);
        }
    }

    private async Task TrySendAsync(
        NotificationRecord record,
        ResolvedRecipient recipient,
        Notification notification,
        NotificationChannel allowed,
        NotificationChannel channel,
        object channelService,
        CancellationToken cancellationToken)
    {
        if ((allowed & channel) == 0)
        {
            return;
        }

        var delivery = new NotificationDelivery
        {
            NotificationRecordId = record.Id,
            UserId = recipient.UserId,
            Channel = channel,
            Status = DeliveryStatus.Pending,
        };

        try
        {
            await SendAsync(channelService, recipient, notification, cancellationToken);
            delivery.Status = DeliveryStatus.Delivered;
        }
        catch (Exception ex)
        {
            delivery.Status = DeliveryStatus.Failed;
            delivery.ErrorMessage = ex.Message;
            _logger.LogWarning(ex, "Notification channel {Channel} failed for user {UserId}.", channel, recipient.UserId);
        }

        await _repository.AddDeliveryAsync(delivery, cancellationToken);
    }

    private static Task SendAsync(
        object channelService,
        ResolvedRecipient recipient,
        Notification notification,
        CancellationToken cancellationToken) => channelService switch
    {
        IRealtimeChannel realtime => realtime.SendAsync(recipient, notification, cancellationToken),
        IPushChannel push => push.SendAsync(recipient, notification, cancellationToken),
        IEmailChannel email => email.SendAsync(recipient, notification, cancellationToken),
        _ => Task.CompletedTask,
    };
}
