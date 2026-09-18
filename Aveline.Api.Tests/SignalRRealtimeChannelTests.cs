using Aveline.Api.Modules.Notifications.Channels;
using Aveline.Api.Modules.Notifications.Hubs;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.SignalR;

namespace Aveline.Api.Tests;

public class SignalRRealtimeChannelTests
{
    private sealed class RecordingClientProxy : IClientProxy
    {
        public string? Method { get; private set; }
        public object?[]? Args { get; private set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Method = method;
            Args = args;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingClients : IHubClients
    {
        public RecordingClientProxy GroupProxy { get; } = new();
        public string? LastGroupName { get; private set; }

        public IClientProxy Group(string groupName)
        {
            LastGroupName = groupName;
            return GroupProxy;
        }

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class RecordingHubContext : IHubContext<NotificationHub>
    {
        public RecordingClients Clients { get; } = new();
        IHubClients IHubContext<NotificationHub>.Clients => Clients;
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private static Notification SampleNotification() => new(
        NotificationType.PaymentConfirmed,
        "Payment confirmed",
        "Order #1234 paid",
        new NotificationTarget(Guid.NewGuid()),
        new Dictionary<string, string?> { ["orderId"] = "ord-1" },
        NotificationChannel.Realtime);

    [Fact]
    public async Task SendAsync_SendsReceiveNotificationToUserGroup_WithDtoPayload()
    {
        var userId = Guid.NewGuid();
        var recipient = new ResolvedRecipient(userId, "user@aveline.lk", true, ContactPreferences.None, []);
        var context = new RecordingHubContext();
        var channel = new SignalRRealtimeChannel(context);

        await channel.SendAsync(recipient, SampleNotification());

        Assert.Equal($"user:{userId}", context.Clients.LastGroupName);
        Assert.Equal("ReceiveNotification", context.Clients.GroupProxy.Method);

        var dto = Assert.IsType<NotificationDto>(context.Clients.GroupProxy.Args![0]);
        Assert.Equal(NotificationType.PaymentConfirmed, dto.Type);
        Assert.Equal("Payment confirmed", dto.Title);
        Assert.Equal("Order #1234 paid", dto.Body);
        Assert.Equal("ord-1", dto.Data["orderId"]);
    }
}
