namespace Aveline.Api.Modules.Statistics.DTOs;

/// <summary>Deployment metadata block (mirrors the readiness payload).</summary>
public sealed record SystemVersionDto(
    string GitSha, string BuildTime, string AssemblyVersion, string Environment);

/// <summary>One dependency readiness check (FR-7.2).</summary>
public sealed record ReadinessCheckDto(string Name, string Status, int DurationMs, string? Message);

public sealed record ReadinessDto(string Status, IReadOnlyList<ReadinessCheckDto> Checks);

/// <summary>One alert row as returned by the alerts endpoint (docs/api/README.md §C.8).</summary>
public sealed record SystemAlertDto(
    Guid Id,
    Guid? RuleId,
    string? RuleName,
    Guid? OrganizationId,
    string MetricName,
    string Severity,
    string Status,
    string Title,
    string? Detail,
    decimal? ObservedValue,
    decimal? Threshold,
    int OccurrenceCount,
    DateTime FiredAt,
    DateTime LastObservedAt,
    DateTime? AcknowledgedAt,
    DateTime? ResolvedAt);

public sealed record SystemAlertPageDto(
    IReadOnlyList<SystemAlertDto> Items, int Page, int PageSize, int Total);

public sealed record SystemMetricPointDto(DateTime WindowStart, decimal? Value);

/// <summary>One named metric series with its dimensions and a data-quality block.</summary>
public sealed record SystemMetricSeriesDto(
    string Metric,
    string? Unit,
    string WindowSize,
    IReadOnlyList<SystemMetricPointDto> Points,
    SystemMetricDataQualityDto DataQuality);

public sealed record SystemMetricDataQualityDto(int Points, bool Measured, IReadOnlyList<string> Omitted);

public sealed record QueueDepthDto(
    long? TelemetryChannelDepth,
    long? EventBusBacklog,
    long? NotificationBacklog,
    long? InboundMessageBacklog,
    long? AgentRunsRunning,
    IReadOnlyList<string> Omitted);

public sealed record ThroughputDto(
    double? RequestsPerSecond,
    double? AgentRunsPerMinute,
    double? BlossomsPerHour,
    IReadOnlyList<string> Omitted);

public sealed record ErrorRateDto(
    double? ErrorRate,
    long RequestCount,
    long ErrorCount,
    string WindowSize,
    bool UnhandledExceptionsMeasured,
    IReadOnlyList<string> Omitted);

public sealed record EventBusStatsDto(
    long Published,
    long Received,
    long Failed,
    long Backlog,
    long? PublishLatencyMs,
    IReadOnlyList<string> Omitted);

public sealed record AlertOverviewDto(int Critical, int Warning, IReadOnlyList<SystemAlertDto> Top);

public sealed record SystemOverviewDto(
    SystemVersionDto Version,
    ReadinessDto Readiness,
    long UptimeSeconds,
    AlertOverviewDto Alerts,
    ThroughputDto Throughput,
    ErrorRateDto Errors,
    QueueDepthDto Queues,
    IReadOnlyList<string> Omitted,
    DateTime GeneratedAt);

/// <summary>Body of <c>POST /system/alerts/{alertId}/acknowledge</c>.</summary>
public sealed record AcknowledgeAlertRequest(string? Note);
