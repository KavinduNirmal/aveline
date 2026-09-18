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
            Calls.Add((deviceToken, title, body, data));
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

        await channel.SendAsync(recipient, NotificationFor());

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

        await channel.SendAsync(Recipient(), NotificationFor());

        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task SendAsync_ClientFailure_PropagatesForDispatcherToRecord()
    {
        var client = new FakeFirebaseMessagingClient { ThrowOnSend = new InvalidOperationException("fcm down") };
        var channel = new FcmPushChannel(client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => channel.SendAsync(Recipient("tok-a"), NotificationFor()));
    }
}
