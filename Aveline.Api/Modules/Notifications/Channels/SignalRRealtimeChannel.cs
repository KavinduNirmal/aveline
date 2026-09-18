using Aveline.Api.Modules.Notifications.Hubs;
using Aveline.Api.Modules.Notifications.Models;
using Microsoft.AspNetCore.SignalR;

namespace Aveline.Api.Modules.Notifications.Channels;

/// <summary>
/// Real-time delivery channel backed by SignalR. Sends each notification to the
/// <c>user:&#123;userId&#125;</c> group (see <see cref="GroupName"/>), which the
/// <see cref="NotificationHub"/> populates on connect. The client method is
/// <c>ReceiveNotification</c> and the payload is a <see cref="NotificationDto"/>.
/// </summary>
public sealed class SignalRRealtimeChannel : IRealtimeChannel
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public SignalRRealtimeChannel(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendAsync(
        ResolvedRecipient recipient,
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        var dto = new NotificationDto(notification.Type, notification.Title, notification.Body, notification.Data);
        await _hubContext.Clients
            .Group(GroupName.ForUser(recipient.UserId))
            .SendAsync("ReceiveNotification", dto, cancellationToken);
    }
}
