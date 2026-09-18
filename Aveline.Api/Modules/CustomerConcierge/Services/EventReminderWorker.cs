using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// Scheduled driver for the event-reminder job (Issue #170). Runs once shortly after startup and
/// then on a daily interval, delegating to the scoped <see cref="IEventReminderService"/>. A
/// failure on any tick is logged and does not stop the worker.
/// </summary>
public sealed class EventReminderWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<EventReminderWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IEventReminderService>();
            await service.ProcessDueEventsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Event-reminder job failed on this tick.");
        }
    }
}
