using System.Diagnostics;
using Aveline.Api.Common.Jobs;

namespace Aveline.Api.Modules.Statistics.Jobs;

/// <summary>
/// Base for the agent statistics jobs. Each run opens its own scope and takes the
/// distributed job lock, and uses a <see cref="PeriodicTimer"/> so a slow pass does not
/// drift (implementation-plan.md §5.2).
/// </summary>
public abstract class StatisticsJobBase(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger logger) : BackgroundService
{
    protected abstract string JobName { get; }

    protected abstract TimeSpan Interval { get; }

    /// <summary>Optional delay before the first pass, used to align a daily job to a wall clock time.</summary>
    protected virtual TimeSpan? InitialDelay => null;

    /// <summary>Performs one pass. Public so tests can run it deterministically.</summary>
    public abstract Task<int> RunAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (InitialDelay is { } delay && delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

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

    private async Task RunLockedAsync(CancellationToken stoppingToken)
    {
        await using var handle = await jobLock.TryAcquireAsync(JobName, cancellationToken: stoppingToken);
        if (handle is null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("JobRun {Job} started.", JobName);

        try
        {
            var processed = await RunAsync(stoppingToken);
            logger.LogInformation(
                "JobRun {Job} completed. durationMs={DurationMs} processed={Processed} failed=0",
                JobName, stopwatch.ElapsedMilliseconds, processed);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "JobRun {Job} failed. durationMs={DurationMs} processed=0 failed=1",
                JobName, stopwatch.ElapsedMilliseconds);
        }
    }
}
