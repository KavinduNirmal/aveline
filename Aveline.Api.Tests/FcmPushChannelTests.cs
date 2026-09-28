using Aveline.Api.Modules.Notifications.Channels;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Tests;

public class FcmPushChannelTests
{
    private sealed class FakeFirebaseMessagingClient : IFirebaseMessagingClient
    {
        public List<(string Token, string Title, string Body, IReadOnlyDictionary<string, string?> Data)> Calls { get; } = [];
        public Exception? ThrowOnSend { get; set; }

        public Task SendAsync(string deviceToken, string title, string body, IReadOnlyDictionary<string, string?> data, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSend is not null)
            {
                throw ThrowOnSend;
            }

            // The real client drops null-valued keys (`FirebaseMessagingClient.cs`);
            // the fake mirrors that so the contract is assertable here.
            Calls.Add((
                deviceToken,
                title,
                body,
                data.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!)));
            return Task.CompletedTask;
        }
    }

    private static ResolvedRecipient Recipient(params string[] tokens) => new(
        Guid.NewGuid(),
        "user@aveline.lk",
        true,
        ContactPreferences.Email,
        tokens);

    private static Notification NotificationFor() => new(
        NotificationType.PaymentConfirmed,
        "Payment confirmed",
        "Order paid",
        new NotificationTarget(Guid.NewGuid()),
        new Dictionary<string, string?> { ["orderId"] = "ord-1" },
        NotificationChannel.Push);

    [Fact]
    public async Task SendAsync_SendsToEveryDeviceToken_WithTitleBodyAndData()
    {
        var client = new FakeFirebaseMessagingClient();
        var channel = new FcmPushChannel(client);
        var recipient = Recipient("tok-a", "tok-b");

        await channel.SendAsync(recipient, NotificationFor(), Guid.NewGuid());

        Assert.Equal(2, client.Calls.Count);
        Assert.Equal("tok-a", client.Calls[0].Token);
        Assert.Equal("tok-b", client.Calls[1].Token);
        Assert.All(client.Calls, c =>
        {
            Assert.Equal("Payment confirmed", c.Title);
            Assert.Equal("Order paid", c.Body);
            Assert.Equal("ord-1", c.Data["orderId"]);
        });
    }

    [Fact]
    public async Task SendAsync_NoDeviceTokens_DoesNothing()
    {
        var client = new FakeFirebaseMessagingClient();
        var channel = new FcmPushChannel(client);

        await channel.SendAsync(Recipient(), NotificationFor(), Guid.NewGuid());

        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task SendAsync_ClientFailure_PropagatesForDispatcherToRecord()
    {
        var client = new FakeFirebaseMessagingClient { ThrowOnSend = new InvalidOperationException("fcm down") };
        var channel = new FcmPushChannel(client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => channel.SendAsync(Recipient("tok-a"), NotificationFor(), Guid.NewGuid()));
    }

    [Fact]
    public async Task SendAsync_MergesTypeAndNotificationId_AndKeepsTheNotificationsOwnData()
    {
        var client = new FakeFirebaseMessagingClient();
        var channel = new FcmPushChannel(client);
        var inboxItemId = Guid.NewGuid();

        await channel.SendAsync(Recipient("tok-a"), NotificationFor(), inboxItemId);

        var call = Assert.Single(client.Calls);
        // The type lets a closed app render the right kind; the id lets a tap mark
        // that notification read. The notification's own keys survive.
        Assert.Equal("PaymentConfirmed", call.Data["type"]);
        Assert.Equal(inboxItemId.ToString(), call.Data["notificationId"]);
        Assert.Equal("ord-1", call.Data["orderId"]);
    }

    [Fact]
    public async Task SendAsync_DropsNullValuedKeys()
    {
        var client = new FakeFirebaseMessagingClient();
        var channel = new FcmPushChannel(client);
        var notification = NotificationFor() with
        {
            Data = new Dictionary<string, string?>
            {
                ["orderId"] = "ord-1",
                ["notOnFile"] = null,
            },
        };

        await channel.SendAsync(Recipient("tok-a"), notification, Guid.NewGuid());

        var call = Assert.Single(client.Calls);
        Assert.True(call.Data.ContainsKey("orderId"));
        Assert.False(call.Data.ContainsKey("notOnFile"));
        Assert.DoesNotContain(call.Data, kv => kv.Value is null);
    }
}
