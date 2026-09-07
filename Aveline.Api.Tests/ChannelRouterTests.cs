using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Services;
using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Tests;

public class ChannelRouterTests
{
    private readonly ChannelRouter _sut = new();

    private static ResolvedRecipient Recipient(
        bool pushEnabled = true,
        ContactPreferences contactPreference = ContactPreferences.None,
        params string[] deviceTokens) => new(
        Guid.NewGuid(),
        "user@aveline.lk",
        pushEnabled,
        contactPreference,
        deviceTokens);

    [Fact]
    public void AllowedChannels_Realtime_AlwaysAllowedWhenRequested()
    {
        var recipient = Recipient(pushEnabled: false);

        var allowed = _sut.AllowedChannels(recipient, NotificationChannel.Realtime);

        Assert.Equal(NotificationChannel.Realtime, allowed);
    }

    [Fact]
    public void AllowedChannels_PushDisabled_ExcludesPush()
    {
        var recipient = Recipient(pushEnabled: false, deviceTokens: "tok");

        var allowed = _sut.AllowedChannels(recipient, NotificationChannel.Push);

        Assert.Equal(NotificationChannel.None, allowed);
    }

    [Fact]
    public void AllowedChannels_PushEnabled_ButNoDeviceToken_ExcludesPush()
    {
        var recipient = Recipient(pushEnabled: true);

        var allowed = _sut.AllowedChannels(recipient, NotificationChannel.Push);

        Assert.Equal(NotificationChannel.None, allowed);
    }

    [Fact]
    public void AllowedChannels_PushEnabled_WithDeviceToken_AllowsPush()
    {
        var recipient = Recipient(pushEnabled: true, deviceTokens: "tok");

        var allowed = _sut.AllowedChannels(recipient, NotificationChannel.Push);

        Assert.Equal(NotificationChannel.Push, allowed);
    }

    [Fact]
    public void AllowedChannels_EmailPreference_AllowsEmail()
    {
        var recipient = Recipient(contactPreference: ContactPreferences.Email);

        var allowed = _sut.AllowedChannels(recipient, NotificationChannel.Email);

        Assert.Equal(NotificationChannel.Email, allowed);
    }

    [Fact]
    public void AllowedChannels_NoEmailPreference_ExcludesEmail()
    {
        var recipient = Recipient(contactPreference: ContactPreferences.None);

        var allowed = _sut.AllowedChannels(recipient, NotificationChannel.Email);

        Assert.Equal(NotificationChannel.None, allowed);
    }

    [Fact]
    public void AllowedChannels_Combined_IntersectsRequestedWithEligible()
    {
        var recipient = Recipient(pushEnabled: true, contactPreference: ContactPreferences.Email, deviceTokens: "tok");

        var allowed = _sut.AllowedChannels(
            recipient,
            NotificationChannel.Realtime | NotificationChannel.Push | NotificationChannel.Email);

        Assert.Equal(
            NotificationChannel.Realtime | NotificationChannel.Push | NotificationChannel.Email,
            allowed);
    }

    [Fact]
    public void AllowedChannels_RequestedPushOnly_DoesNotAddUnrequestedChannels()
    {
        var recipient = Recipient(pushEnabled: true, contactPreference: ContactPreferences.Email, deviceTokens: "tok");

        var allowed = _sut.AllowedChannels(recipient, NotificationChannel.Push);

        Assert.Equal(NotificationChannel.Push, allowed);
    }
}
