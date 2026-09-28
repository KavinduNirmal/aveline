using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Notifications.Metrics;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Notifications.Jobs;

/// <summary>
/// Queries the notification tables and publishes the derived family (Slice 7, plan §6.5).
/// </summary>
/// <remarks>
/// The window is bounded (24 h) so the queries stay cheap and the panels mean "today", and the
/// inbox backlog is counted separately over all time because it is a current-state gauge rather
/// than a windowed rate. The HTTP routes for this family are deferred — see
/// <see cref="NotificationMetrics"/> and <c>docs/backend/observability.md</c>.
/// </remarks>
public sealed class NotificationMetricCollector(
    IServiceScopeFactory scopeFactory,
    NotificationMetrics metrics,
    IConfiguration configuration,
    ILogger<NotificationMetricCollector> logger) : BackgroundService
{
    /// <summary>The window the rate and volume series are computed over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunAsync(stoppingToken);

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs one pass. Public so tests can drive it deterministically.</summary>
    public async Task<NotificationMetricSnapshot> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var since = DateTime.UtcNow - Window;

        try
        {
            var records = await db.NotificationRecords
                .AsNoTracking()
                .Where(record => record.CreatedAt >= since)
                .ToListAsync(cancellationToken);

            var deliveries = await db.NotificationDeliveries
                .AsNoTracking()
                .Where(delivery => delivery.AttemptedAt >= since)
                .ToListAsync(cancellationToken);

            var inbox = await db.UserNotifications
                .AsNoTracking()
                .Where(item => item.CreatedAt >= since)
                .ToListAsync(cancellationToken);

            var tokens = await db.UserDeviceTokens
                .AsNoTracking()
                .Where(token => token.IsActive)
                .ToListAsync(cancellationToken);

            // The backlog is a current-state gauge, not a windowed rate: it counts every unread,
            // undismissed item, which is a different denominator from S-36's notification_backlog.
            var backlog = await db.UserNotifications
                .AsNoTracking()
                .LongCountAsync(item => item.ReadAt == null && item.DismissedAt == null, cancellationToken);

            var snapshot = NotificationMetricSnapshot.Derive(
                deliveries,
                inbox,
                records,
                tokens,
                FirebaseConfiguration.IsConfigured(configuration),
                Math.Max(1, configuration.GetValue("Telemetry:MinSampleForPercentile", 20)))
                with { InboxBacklog = backlog };

            metrics.Publish(snapshot);
            return snapshot;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A transient read failure omits the windowed series; the FCM gauge still reports.
            logger.LogWarning(exception, "Notification metric collection failed; the windowed series are omitted.");
            return NotificationMetricSnapshot.Empty(FirebaseConfiguration.IsConfigured(configuration));
        }
    }
}
