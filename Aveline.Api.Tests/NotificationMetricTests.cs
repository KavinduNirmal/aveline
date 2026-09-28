using Aveline.Api.Configurations;
using Aveline.Api.Modules.Notifications.Metrics;
using Aveline.Api.Modules.Notifications.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 7 (plan §6.5). The family's four constraints: the inbox backlog is a different
/// denominator from S-36's delivery backlog, the percentile is absent rather than zero below the
/// sample floor, the FCM gauge is 1/0 from the channel selection, and the delivery counter comes
/// from the dispatcher.
/// </summary>
[Collection(MetricsExpositionCollection.Name)]
public class NotificationMetricTests
{
    private static NotificationRecord Record(NotificationType type, DateTime createdAt) => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationId = Guid.NewGuid(),
        Type = type,
        Title = "t",
        Body = "b",
        CreatedAt = createdAt,
    };

    private static UserNotification Inbox(Guid recordId, DateTime createdAt, DateTime? readAt = null, DateTime? dismissedAt = null) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = Guid.NewGuid(),
        NotificationRecordId = recordId,
        CreatedAt = createdAt,
        ReadAt = readAt,
        DismissedAt = dismissedAt,
    };

    private static NotificationDelivery Delivery(Guid recordId, NotificationChannel channel, DeliveryStatus status, string? error = null) => new()
    {
        Id = Guid.CreateVersion7(),
        NotificationRecordId = recordId,
        UserId = Guid.NewGuid(),
        Channel = channel,
        Status = status,
        ErrorMessage = error,
        AttemptedAt = DateTime.UtcNow,
    };

    [Fact]
    public void InboxBacklog_CountsOnlyUnreadAndUndismissedItems()
    {
        var record = Record(NotificationType.NewMessage, DateTime.UtcNow);

        // S-36's notification_backlog counts delivery rows with Status = Pending; this counts work
        // a user has not acted upon. Same words, different denominator — never merge them.
        var inbox = new[]
        {
            Inbox(record.Id, DateTime.UtcNow),
            Inbox(record.Id, DateTime.UtcNow, readAt: DateTime.UtcNow),
            Inbox(record.Id, DateTime.UtcNow, dismissedAt: DateTime.UtcNow),
            Inbox(record.Id, DateTime.UtcNow),
        };

        var snapshot = NotificationMetricSnapshot.Derive(
            [], inbox, [record], [], fcmCredentialConfigured: false, minSampleForPercentile: 20);

        snapshot.InboxBacklog.Should().Be(2);
    }

    [Fact]
    public void TimeToReadP50_IsAbsentBelowTheSampleFloor()
    {
        var record = Record(NotificationType.NewMessage, DateTime.UtcNow.AddMinutes(-10));
        var inbox = new[] { Inbox(record.Id, record.CreatedAt, readAt: DateTime.UtcNow) };

        var snapshot = NotificationMetricSnapshot.Derive(
            [], inbox, [record], [], fcmCredentialConfigured: false, minSampleForPercentile: 20);

        snapshot.TimeToReadP50Minutes.Should().BeNull("a zero would be the lie the plan forbids");
    }

    [Fact]
    public void TimeToReadP50_IsComputedOnceTheFloorIsMet()
    {
        var record = Record(NotificationType.NewMessage, DateTime.UtcNow.AddHours(-1));
        var inbox = Enumerable.Range(1, 21)
            .Select(index => Inbox(record.Id, record.CreatedAt, readAt: record.CreatedAt.AddMinutes(index)))
            .ToArray();

        var snapshot = NotificationMetricSnapshot.Derive(
            [], inbox, [record], [], fcmCredentialConfigured: false, minSampleForPercentile: 20);

        snapshot.TimeToReadP50Minutes.Should().BeApproximately(11, 0.001);
    }

    [Fact]
    public void DeliverySuccessRate_OmitsAChannelWithNoTerminalAttempt()
    {
        var record = Record(NotificationType.NewMessage, DateTime.UtcNow);
        var deliveries = new[]
        {
            Delivery(record.Id, NotificationChannel.Push, DeliveryStatus.Delivered),
            Delivery(record.Id, NotificationChannel.Push, DeliveryStatus.Failed, "timed out"),
            Delivery(record.Id, NotificationChannel.Email, DeliveryStatus.Pending),
        };

        var snapshot = NotificationMetricSnapshot.Derive(
            deliveries, [], [record], [], fcmCredentialConfigured: false, minSampleForPercentile: 20);

        snapshot.DeliverySuccessRateByChannel.Should().ContainKey("Push").WhoseValue.Should().Be(0.5);
        snapshot.DeliverySuccessRateByChannel.Should().NotContainKey("Email");
    }

    [Fact]
    public void ErrorClassesAreBoundedAndNeverCarryFreeText()
    {
        NotificationMetricSnapshot.NormaliseErrorClass("Connection timed out after 30s").Should().Be("timeout");
        NotificationMetricSnapshot.NormaliseErrorClass("Invalid registration token abc123").Should().Be("invalid_token");
        NotificationMetricSnapshot.NormaliseErrorClass("401 Unauthorized: bad credentials").Should().Be("auth");
        NotificationMetricSnapshot.NormaliseErrorClass("Service unavailable").Should().Be("unavailable");
        NotificationMetricSnapshot.NormaliseErrorClass("weird thing happened").Should().Be("other");
        NotificationMetricSnapshot.NormaliseErrorClass(null).Should().Be("unknown");
    }

    [Fact]
    public void PushDispatchFailures_AreGroupedByDevicePlatform()
    {
        var record = Record(NotificationType.NewMessage, DateTime.UtcNow);
        var userId = Guid.NewGuid();
        var deliveries = new[]
        {
            new NotificationDelivery { Channel = NotificationChannel.Push, Status = DeliveryStatus.Failed, UserId = userId },
        };
        var tokens = new[]
        {
            new UserDeviceToken { UserId = userId, Platform = DevicePlatform.Android, IsActive = true },
            new UserDeviceToken { UserId = userId, Platform = DevicePlatform.IOS, IsActive = true },
        };

        var snapshot = NotificationMetricSnapshot.Derive(
            deliveries, [], [record], tokens, fcmCredentialConfigured: true, minSampleForPercentile: 20);

        snapshot.FcmCredentialConfigured.Should().BeTrue();
        snapshot.PushDispatchFailuresByPlatform.Should().ContainKey("Android").WhoseValue.Should().Be(1);
        snapshot.PushDispatchFailuresByPlatform.Should().ContainKey("IOS").WhoseValue.Should().Be(1);
    }

    [Fact]
    public void VolumeByType_UsesTheNotificationTypeNotAFreeTextTitle()
    {
        var records = new[]
        {
            Record(NotificationType.NewMessage, DateTime.UtcNow),
            Record(NotificationType.NewMessage, DateTime.UtcNow),
            Record(NotificationType.PaymentConfirmed, DateTime.UtcNow),
        };

        var snapshot = NotificationMetricSnapshot.Derive(
            [], [], records, [], fcmCredentialConfigured: false, minSampleForPercentile: 20);

        snapshot.VolumeByType["NewMessage"].Should().Be(2);
        snapshot.VolumeByType["PaymentConfirmed"].Should().Be(1);
    }

    [Fact]
    public async Task NotificationSeriesArePublishedUnderTheirCatalogNames()
    {
        var body = await ScrapeBodyAsync(() =>
        {
            var metrics = new NotificationMetrics(fcmCredentialConfigured: true);
            metrics.Publish(PopulatedSnapshot());
            metrics.RecordDelivery(NotificationChannel.Realtime, DeliveryStatus.Delivered);
            return metrics;
        });
        var typeNames = MetricsNamingTests.TypeNames(body);

        // Every notification entry in the catalog must be scraped under its Prometheus name.
        typeNames.Should().Contain(MetricsCatalog.NotificationMetrics.Select(metric => metric.PrometheusName));
    }

    [Fact]
    public async Task TheFcmGaugeReadsZeroOnTheLoggingFallbackAndOneWhenConfigured()
    {
        var body = await ScrapeBodyAsync(() => new NotificationMetrics(fcmCredentialConfigured: false));
        body.Should().MatchRegex(@"aveline_notification_fcm_credential_configured\{[^}]*\} 0");

        var configuredBody = await ScrapeBodyAsync(() => new NotificationMetrics(fcmCredentialConfigured: true));
        configuredBody.Should().MatchRegex(@"aveline_notification_fcm_credential_configured\{[^}]*\} 1");
    }

    /// <summary>A snapshot with a value for every series, so every instrument publishes.</summary>
    private static NotificationMetricSnapshot PopulatedSnapshot() => new(
        FcmCredentialConfigured: true,
        DeliverySuccessRateByChannel: new Dictionary<string, double> { ["Realtime"] = 1 },
        InboxReadRateByType: new Dictionary<string, double> { ["NewMessage"] = 1 },
        DismissRateByType: new Dictionary<string, double> { ["NewMessage"] = 0 },
        TimeToReadP50Minutes: 4.2,
        FailureReasonsByChannelAndClass: new Dictionary<string, long> { ["Push|timeout"] = 1 },
        VolumeByType: new Dictionary<string, long> { ["NewMessage"] = 3 },
        InboxBacklog: 2,
        PushDispatchFailuresByPlatform: new Dictionary<string, long> { ["Android"] = 1 });

    /// <summary>
    /// Builds the host FIRST and creates the instruments inside it. A synchronous counter
    /// measurement recorded before a meter provider exists is dropped, and an observable gauge
    /// whose instance is collected stops reporting.
    /// </summary>
    private static async Task<string> ScrapeBodyAsync(Func<NotificationMetrics> createMetrics)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenTelemetry().WithMetrics(m => m
            .AddMeter(NotificationMetrics.MeterName)
            .AddPrometheusExporter());

        var app = builder.Build();
        app.MapPrometheusScrapingEndpoint("/metrics");

        await app.StartAsync();
        var metrics = createMetrics();
        try
        {
            var client = app.GetTestClient();
            var body = await client.GetStringAsync("/metrics");
            GC.KeepAlive(metrics);
            return body;
        }
        finally
        {
            metrics.Dispose();
            await app.StopAsync();
        }
    }
}
