using System.Diagnostics;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Repositories;
using Aveline.Api.Modules.Statistics.Telemetry;
using Aveline.Api.Modules.SystemHealth.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// Default <see cref="ISystemStatisticsService"/>. Reads the durable system metric samples,
/// the live in-process counters and the health subsystem; anything it cannot determine is
/// reported in an <c>omitted</c> list rather than as a zero (BR-7.10).
/// </summary>
public sealed class SystemStatisticsService : ISystemStatisticsService
{
    private static readonly DateTime ProcessStartedAtUtc = ResolveProcessStart();

    private readonly AppDbContext _db;
    private readonly ISystemMetricRepository _metrics;
    private readonly TelemetryChannel _telemetry;
    private readonly EventBusMetrics _eventBus;
    private readonly HealthCheckService _health;
    private readonly DeploymentInfoProvider _deployment;

    public SystemStatisticsService(
        AppDbContext db,
        ISystemMetricRepository metrics,
        TelemetryChannel telemetry,
        EventBusMetrics eventBus,
        HealthCheckService health,
        DeploymentInfoProvider deployment)
    {
        _db = db;
        _metrics = metrics;
        _telemetry = telemetry;
        _eventBus = eventBus;
        _health = health;
        _deployment = deployment;
    }

    public async Task<SystemOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var readiness = await GetReadinessAsync(cancellationToken);
        var queues = await GetQueuesAsync(cancellationToken);
        var throughput = await GetThroughputAsync(now.AddMinutes(-15), now, "minute", cancellationToken);
        var errors = await GetErrorsAsync(now.AddMinutes(-15), now, "minute", cancellationToken);

        var open = _db.SystemAlerts.AsNoTracking().Where(alert => alert.Status != AlertStatus.Resolved);
        var critical = await open.CountAsync(alert => alert.Severity == AlertSeverity.Critical, cancellationToken);
        var warning = await open.CountAsync(alert => alert.Severity == AlertSeverity.Warning, cancellationToken);
        var top = await QueryAlertsAsync(
            status: null, severity: null, ruleId: null, page: 1, pageSize: 5, openOnly: true,
            cancellationToken);

        var omitted = queues.Omitted
            .Concat(errors.Omitted)
            .Concat(throughput.Omitted)
            .Distinct()
            .ToList();

        return new SystemOverviewDto(
            new SystemVersionDto(
                _deployment.Current.GitSha,
                _deployment.Current.BuildTime,
                _deployment.Current.AssemblyVersion,
                _deployment.Current.Environment),
            readiness,
            (long)Math.Max(0, (now - ProcessStartedAtUtc).TotalSeconds),
            new AlertOverviewDto(critical, warning, top.Items),
            throughput,
            errors,
            queues,
            omitted,
            now);
    }

    public async Task<SystemMetricSeriesDto> GetMetricSeriesAsync(
        string metric, DateTime from, DateTime to, string? windowSize,
        CancellationToken cancellationToken = default)
    {
        var samples = await _metrics.QueryAsync(metric, from, to, windowSize, cancellationToken);
        var points = samples
            .Select(sample => new SystemMetricPointDto(sample.WindowStart, sample.Value))
            .ToList();
        var unit = samples.Count > 0 ? samples[0].Unit : null;
        var omitted = points.Count == 0 ? new[] { "no_samples_in_window" } : [];

        return new SystemMetricSeriesDto(
            metric,
            unit,
            windowSize ?? "instant",
            points,
            new SystemMetricDataQualityDto(points.Count, points.Count > 0, omitted));
    }

    public async Task<QueueDepthDto> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        var counters = _eventBus.Snapshot();
        var published = counters.GetValueOrDefault("aveline.events.published");
        var received = counters.GetValueOrDefault("aveline.events.received");

        var notificationBacklog = await _db.NotificationDeliveries
            .AsNoTracking()
            .LongCountAsync(delivery => delivery.Status == DeliveryStatus.Pending, cancellationToken);

        var agentRunsRunning = await _db.AgentWorkflowRuns
            .AsNoTracking()
            .LongCountAsync(run => run.Status == AgentRunStatus.Running, cancellationToken);

        // InboundMessageLogs has no processed marker, so the inbound backlog is unmeasurable.
        var omitted = new List<string> { "inbound_message_backlog" };

        return new QueueDepthDto(
            _telemetry.PendingCount,
            Math.Max(0, published - received),
            notificationBacklog,
            null,
            agentRunsRunning,
            omitted);
    }

    public async Task<ErrorRateDto> GetErrorsAsync(
        DateTime from, DateTime to, string windowSize, CancellationToken cancellationToken = default)
    {
        var storedWindow = windowSize == "day" ? "day" : "hour";
        var rows = await _db.ApiRequestMetrics
            .AsNoTracking()
            .Where(metric => metric.WindowStart >= from && metric.WindowStart <= to
                             && metric.WindowSize == storedWindow)
            .Select(metric => new { metric.RequestCount, metric.ErrorCount, metric.StatusClass })
            .ToListAsync(cancellationToken);

        var requests = rows.Sum(row => row.RequestCount);
        var serverErrors = rows.Where(row => row.StatusClass == "5xx").Sum(row => row.ErrorCount);
        var errorRate = requests > 0 ? (double)serverErrors / requests : (double?)null;

        return new ErrorRateDto(
            errorRate,
            requests,
            serverErrors,
            windowSize,
            UnhandledExceptionsMeasured: false,
            Omitted: ["unhandled_exceptions"]);
    }

    public async Task<ThroughputDto> GetThroughputAsync(
        DateTime from, DateTime to, string windowSize, CancellationToken cancellationToken = default)
    {
        var storedWindow = windowSize == "day" ? "day" : "hour";
        var requests = await _db.ApiRequestMetrics
            .AsNoTracking()
            .Where(metric => metric.WindowStart >= from && metric.WindowStart <= to
                             && metric.WindowSize == storedWindow)
            .SumAsync(metric => (long?)metric.RequestCount, cancellationToken) ?? 0;

        var windowSeconds = Math.Max(1d, (to - from).TotalSeconds);
        var requestsPerSecond = requests / windowSeconds;

        var runs = await _db.AgentWorkflowRuns
            .AsNoTracking()
            .Where(run => run.StartedAt >= from && run.StartedAt <= to)
            .Select(run => run.BlossomUnits)
            .ToListAsync(cancellationToken);

        var windowMinutes = Math.Max(1d, (to - from).TotalMinutes);
        var agentRunsPerMinute = runs.Count / windowMinutes;
        var blossomsPerHour = runs.Count > 0
            ? (double)(runs.Sum() / (decimal)Math.Max(1d, (to - from).TotalHours))
            : (double?)null;

        return new ThroughputDto(
            requestsPerSecond,
            agentRunsPerMinute,
            blossomsPerHour,
            runs.Count > 0 ? [] : ["blossoms_per_hour"]);
    }

    public Task<EventBusStatsDto> GetEventBusAsync(CancellationToken cancellationToken = default)
    {
        var counters = _eventBus.Snapshot();
        var published = counters.GetValueOrDefault("aveline.events.published");
        var received = counters.GetValueOrDefault("aveline.events.received");
        var failed = counters.GetValueOrDefault("aveline.events.failed");

        return Task.FromResult(new EventBusStatsDto(
            published,
            received,
            failed,
            Math.Max(0, published - received),
            PublishLatencyMs: null,
            Omitted: ["publish_latency_ms"]));
    }

    public async Task<SystemAlertPageDto> GetAlertsAsync(
        string? status, string? severity, Guid? ruleId, int page, int pageSize,
        CancellationToken cancellationToken = default)
        => await QueryAlertsAsync(status, severity, ruleId, page, pageSize, openOnly: false, cancellationToken);

    private async Task<SystemAlertPageDto> QueryAlertsAsync(
        string? status, string? severity, Guid? ruleId, int page, int pageSize, bool openOnly,
        CancellationToken cancellationToken)
    {
        var query = _db.SystemAlerts.AsNoTracking();

        if (openOnly)
        {
            query = query.Where(alert => alert.Status != AlertStatus.Resolved);
        }

        if (status is not null && Enum.TryParse<AlertStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(alert => alert.Status == parsedStatus);
        }

        if (severity is not null && Enum.TryParse<AlertSeverity>(severity, ignoreCase: true, out var parsedSeverity))
        {
            query = query.Where(alert => alert.Severity == parsedSeverity);
        }

        if (ruleId is { } id)
        {
            query = query.Where(alert => alert.RuleId == id);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(alert => alert.FiredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var ruleIds = rows.Where(alert => alert.RuleId is not null)
            .Select(alert => alert.RuleId!.Value)
            .Distinct()
            .ToList();
        var ruleNames = await _db.SystemAlertRules
            .AsNoTracking()
            .Where(rule => ruleIds.Contains(rule.Id))
            .ToDictionaryAsync(rule => rule.Id, rule => rule.Name, cancellationToken);

        var items = rows.Select(alert => new SystemAlertDto(
            alert.Id,
            alert.RuleId,
            alert.RuleId is { } rule && ruleNames.TryGetValue(rule, out var name) ? name : null,
            alert.OrganizationId,
            alert.MetricName,
            alert.Severity.ToString(),
            alert.Status.ToString(),
            alert.Title,
            alert.Detail,
            alert.ObservedValue,
            alert.Threshold,
            alert.OccurrenceCount,
            alert.FiredAt,
            alert.LastObservedAt,
            alert.AcknowledgedAt,
            alert.ResolvedAt)).ToList();

        return new SystemAlertPageDto(items, page, pageSize, total);
    }

    private async Task<ReadinessDto> GetReadinessAsync(CancellationToken cancellationToken)
    {
        var report = await _health.CheckHealthAsync(cancellationToken);
        var checks = report.Entries
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new ReadinessCheckDto(
                entry.Key,
                entry.Value.Status.ToString(),
                (int)Math.Round(entry.Value.Duration.TotalMilliseconds),
                string.IsNullOrWhiteSpace(entry.Value.Description) ? null : entry.Value.Description))
            .ToList();

        return new ReadinessDto(report.Status.ToString(), checks);
    }

    private static DateTime ResolveProcessStart()
    {
        try
        {
            return Process.GetCurrentProcess().StartTime.ToUniversalTime();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return DateTime.UtcNow;
        }
    }
}
