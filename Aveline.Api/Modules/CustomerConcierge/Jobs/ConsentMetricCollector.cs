using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Metrics;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.CustomerConcierge.Jobs;

/// <summary>
/// Publishes the consent-state snapshot the plan's §9.1 consent family asks for
/// (<c>consent_customers_total</c> / <c>consent_state_distribution</c> as one series keyed by
/// status). It runs on the same pattern as
/// <c>NotificationMetricCollector</c>, and for the same recorded reason: an aggregating series that
/// needs a query is an honest snapshot gauge, not a scrape-time derivation of a counter.
/// </summary>
/// <remarks>
/// <para>
/// <b>One pass = two counts, grouped in the database.</b> The query is a grouped <c>COUNT(*)</c> over
/// <c>CustomerConsents</c>, which the <c>(OrganizationId, ConsentStatus)</c> index serves as an
/// index-only scan; the tenant dimension is deliberately absent because a per-tenant series is
/// forbidden (<c>MetricsCatalog.ForbiddenLabelKeys</c>).
/// </para>
/// <para>
/// <b>A failed pass publishes nothing.</b> A database outage leaves the previous snapshot in place
/// rather than reporting zero, so a panel never shows "nobody consented" because the database was
/// briefly unavailable (BR-7.10's spirit: an unknown value is omitted, never recorded as 0).
/// </para>
/// </remarks>
public sealed class ConsentMetricCollector : BackgroundService
{
    /// <summary>How often the snapshot is refreshed. The gauge is current state, not a window.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConsentMetrics _metrics;
    private readonly ILogger<ConsentMetricCollector> _logger;

    public ConsentMetricCollector(
        IServiceScopeFactory scopeFactory,
        ConsentMetrics metrics,
        ILogger<ConsentMetricCollector> logger)
    {
        _scopeFactory = scopeFactory;
        _metrics = metrics;
        _logger = logger;
    }

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

    /// <summary>Runs one pass. Public so a test can drive it deterministically.</summary>
    public async Task<IReadOnlyDictionary<string, long>> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var counts = await db.CustomerConsents
                .AsNoTracking()
                .GroupBy(consent => consent.ConsentStatus)
                .Select(group => new { Status = group.Key, Count = (long)group.Count() })
                .ToListAsync(cancellationToken);

            var byStatus = counts.ToDictionary(
                item => item.Status ?? "unknown",
                item => item.Count,
                StringComparer.Ordinal);

            _metrics.PublishByStatus(byStatus);
            return byStatus;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed pass leaves the previous snapshot; the series goes stale, never wrong.
            _logger.LogError(ex, "The consent-state metric pass failed; the previous snapshot stands.");
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }
    }
}
