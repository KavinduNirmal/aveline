using System.Diagnostics;
using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Notifications.Jobs;

/// <summary>
/// Purges inbox rows the user is finished with: dismissed rows after
/// <c>Notifications:DismissedRetentionDays</c> (default 30) and read rows after
/// <c>Notifications:ReadRetentionDays</c> (default 180). The audit trail —
/// <see cref="Models.NotificationRecord"/> and <see cref="Models.NotificationDelivery"/> — is
/// deliberately never touched; it needs a compliance answer before anything deletes it.
///
/// The pass is idempotent and guarded by the shared distributed job lock, so two
/// instances cannot purge at once. It follows the module-local
/// <see cref="BackgroundService"/> shape the conversations sweep established: a
/// <see cref="PeriodicTimer"/>, no hosted-service abstraction, and a public one-pass
/// method a test can drive.
/// </summary>
public sealed class NotificationRetentionJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<NotificationRetentionJob> logger) : BackgroundService
{
    /// <summary>The name the distributed job lock is taken under.</summary>
    public const string JobName = "notification-retention";

    public const int DefaultDismissedRetentionDays = 30;
    public const int DefaultReadRetentionDays = 180;

    /// <summary>How often the purge runs.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunLockedAsync(stoppingToken);

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

    /// <summary>
    /// Runs one pass under the distributed lock. Returns 0 when another holder has the
    /// lock, so a second instance is a no-op rather than a concurrent purge.
    /// </summary>
    public async Task<int> RunLockedAsync(CancellationToken cancellationToken = default)
    {
        await using var handle = await jobLock.TryAcquireAsync(JobName, cancellationToken: cancellationToken);
        if (handle is null)
        {
            return 0;
        }

        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("JobRun {Job} started.", JobName);

        try
        {
            var processed = await RunAsync(cancellationToken);
            logger.LogInformation(
                "JobRun {Job} completed. durationMs={DurationMs} processed={Processed} failed=0",
                JobName, stopwatch.ElapsedMilliseconds, processed);
            return processed;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "JobRun {Job} failed. durationMs={DurationMs} processed=0 failed=1",
                JobName, stopwatch.ElapsedMilliseconds);
            return 0;
        }
    }

    /// <summary>One purge pass, public so a test can drive it deterministically.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var dismissedDays = Math.Max(
            0, configuration.GetValue("Notifications:DismissedRetentionDays", DefaultDismissedRetentionDays));
        var readDays = Math.Max(
            0, configuration.GetValue("Notifications:ReadRetentionDays", DefaultReadRetentionDays));

        var now = DateTime.UtcNow;
        var dismissedCutoff = now.AddDays(-dismissedDays);
        var readCutoff = now.AddDays(-readDays);

        var stale = await db.UserNotifications
            .Where(row =>
                (row.DismissedAt != null && row.DismissedAt < dismissedCutoff)
                || (row.ReadAt != null && row.ReadAt < readCutoff))
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        db.UserNotifications.RemoveRange(stale);
        await db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }
}
