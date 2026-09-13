using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.Statistics.Models;

/// <summary>
/// One durable metric sample (domain-model.md §8.1, FR-7.5–FR-7.10). The table is a
/// fallback for metrics that cannot be scraped from Prometheus, not a primary metrics
/// store. Exactly one of <see cref="ValueDecimal"/> / <see cref="ValueBigint"/> is set,
/// enforced by <c>CK_SystemMetricSamples_Value</c>.
/// </summary>
/// <remarks>
/// The primary key is a <c>bigint</c> identity because this is a high-volume append-heavy
/// table and the key is never exposed to clients (domain-model.md §8.1, C-4).
/// </remarks>
public class SystemMetricSample
{
    public long Id { get; set; }

    /// <summary>Metric name in <c>aveline.&lt;subsystem&gt;.&lt;measure&gt;</c> form (BR-7.8).</summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>Canonicalised dimension payload, e.g. <c>{}</c> or <c>{"queueName":"telemetry"}</c>.</summary>
    public string DimensionsJson { get; set; } = "{}";

    /// <summary>SHA-256 of the canonicalised dimensions, used for the uniqueness key.</summary>
    public string DimensionHash { get; set; } = string.Empty;

    /// <summary>Set for ratios and fractional measures; mutually exclusive with <see cref="ValueBigint"/>.</summary>
    public decimal? ValueDecimal { get; set; }

    /// <summary>Set for integral counts; mutually exclusive with <see cref="ValueDecimal"/>.</summary>
    public long? ValueBigint { get; set; }

    /// <summary><c>count</c>, <c>ms</c>, <c>bytes</c>, <c>ratio</c> or <c>percent</c>.</summary>
    public string Unit { get; set; } = "count";

    /// <summary>Start of the covered window, UTC.</summary>
    public DateTime WindowStart { get; set; }

    /// <summary><c>instant</c>, <c>minute</c>, <c>hour</c> or <c>day</c>.</summary>
    public string WindowSize { get; set; } = "instant";

    /// <summary>When the sample was taken, UTC.</summary>
    public DateTime SampledAt { get; set; } = DateTime.UtcNow;

    /// <summary>The numeric value with the precision the caller needs.</summary>
    [JsonIgnore]
    public decimal? Value => ValueDecimal ?? (ValueBigint is { } bigint ? bigint : null);
}

/// <summary>Aggregations an alert rule may apply over its evaluation window (domain-model.md §8.2).</summary>
public enum AlertAggregation
{
    Avg,
    Max,
    Min,
    Sum,
    Rate,
    Count,
}

/// <summary>Comparison operators an alert rule may use (domain-model.md §8.2).</summary>
public enum AlertComparisonOperator
{
    Gt,
    Gte,
    Lt,
    Lte,
    Eq,
}

/// <summary>Alert severity (FR-7.12).</summary>
public enum AlertSeverity
{
    Info,
    Warning,
    Critical,
}

/// <summary>Alert lifecycle status (FR-7.12).</summary>
public enum AlertStatus
{
    Firing,
    Acknowledged,
    Resolved,
}

/// <summary>
/// A configurable, data-driven alert rule (domain-model.md §8.2, FR-7.11). Rules are
/// evaluated on a schedule by the alert evaluation job; the status they produce lives in
/// <see cref="SystemAlert"/>.
/// </summary>
public class SystemAlertRule
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Unique, human-readable rule name, e.g. <c>blossom.ledger.drift</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The metric this rule watches.</summary>
    public string MetricName { get; set; } = string.Empty;

    public AlertAggregation Aggregation { get; set; } = AlertAggregation.Avg;

    public AlertComparisonOperator ComparisonOperator { get; set; } = AlertComparisonOperator.Gt;

    /// <summary>Threshold the aggregate is compared against.</summary>
    public decimal Threshold { get; set; }

    /// <summary>Evaluation window in seconds.</summary>
    public int WindowSeconds { get; set; } = 300;

    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Minimum seconds between two fires of the same rule (BR-7.6).</summary>
    public int CooldownSeconds { get; set; } = 300;

    /// <summary>Upper bound on alerts fired per rolling hour (BR-7.6, alert-storm guard).</summary>
    public int MaxAlertsPerHour { get; set; } = 10;

    /// <summary>Canonical boutique roles targeted through <c>IRecipientResolver</c> (BR-7.11).</summary>
    public List<string> TargetRoles { get; set; } = ["owner"];

    /// <summary>Optional dimension narrowing as canonical JSON, e.g. <c>{"queueName":"telemetry"}</c>.</summary>
    public string? DimensionFiltersJson { get; set; }

    /// <summary>No real user owns a seeded system rule; <see cref="Guid.Empty"/> marks one.</summary>
    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastTriggeredAt { get; set; }
}

/// <summary>
/// One fired alert instance (domain-model.md §8.3). <see cref="ConsecutiveOkCount"/> is the
/// persisted auto-resolution counter (BR-7.7): it is incremented on every below-threshold
/// evaluation and reset to zero whenever the condition is still breaching, so the alert
/// resolves after <c>Observability:AutoResolveConsecutiveOk</c> consecutive OK evaluations.
/// </summary>
public class SystemAlert
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary><c>NULL</c> = ad-hoc alert not produced by a rule.</summary>
    public Guid? RuleId { get; set; }

    /// <summary><c>NULL</c> = system-wide.</summary>
    public Guid? OrganizationId { get; set; }

    public string MetricName { get; set; } = string.Empty;

    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;

    public AlertStatus Status { get; set; } = AlertStatus.Firing;

    public string Title { get; set; } = string.Empty;

    public string? Detail { get; set; }

    public decimal? ObservedValue { get; set; }

    public decimal? Threshold { get; set; }

    /// <summary>Incremented while the rule is in cooldown instead of re-firing (BR-7.6).</summary>
    public int OccurrenceCount { get; set; } = 1;

    /// <summary>Consecutive below-threshold evaluations (BR-7.7).</summary>
    public int ConsecutiveOkCount { get; set; }

    public DateTime FiredAt { get; set; } = DateTime.UtcNow;

    public DateTime LastObservedAt { get; set; } = DateTime.UtcNow;

    public DateTime? AcknowledgedAt { get; set; }

    public Guid? AcknowledgedByUserId { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public Guid? ResolvedByUserId { get; set; }

    public string? ResolutionNote { get; set; }

    /// <summary>Set when a critical alert dispatched a notification (BR-7.11).</summary>
    public Guid? NotificationRecordId { get; set; }

    public SystemAlertRule? Rule { get; set; }
}
