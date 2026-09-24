using System.Diagnostics.Metrics;

namespace Aveline.Api.Configurations;

/// <summary>The instrument shape behind an <see cref="ExportedMetric"/>.</summary>
internal enum ExportedMetricKind
{
    Gauge,
    Counter,
    Histogram,
}

/// <summary>Which producer owns the instrument, so the naming test can drive each one.</summary>
internal enum ExportedMetricFamily
{
    /// <summary>The twenty bridged business metrics (AvelineMetrics).</summary>
    Business,

    /// <summary>The event-bus counters and publish-latency histogram (EventBusMetrics).</summary>
    Eventing,

    /// <summary>The notification metric family (NotificationMetrics, Slice 7).</summary>
    Notification,
}

/// <summary>
/// One authored mapping from the internal dotted metric name (the Postgres
/// <c>SystemMetricSamples.MetricName</c> value and the key the seeded alert-rule guard watches)
/// to the Prometheus series name a rule, dashboard or panel must use.
/// </summary>
/// <param name="DottedName"><c>aveline.queue.telemetry_channel</c> — never a Prometheus name.</param>
/// <param name="PrometheusName">
/// <c>aveline_queue_telemetry_channel_count</c> — what a PromQL expression must say.
/// </param>
/// <param name="Unit">
/// The OpenTelemetry instrument unit. It is part of the mapping because the pinned exporter
/// translates it into a suffix (<c>ms → milliseconds</c>) and appends it unless the sanitised
/// name already ends with it. Keep it here so a divergent name is visible next to its cause.
/// </param>
/// <param name="Kind">Gauge, counter or histogram — histograms publish three series suffixes.</param>
internal sealed record ExportedMetric(
    string DottedName,
    string PrometheusName,
    string Unit,
    ExportedMetricKind Kind,
    ExportedMetricFamily Family);

/// <summary>
/// The single place a Prometheus series name is authored (plan §6.1). Three consumers need it and
/// one — the exporter's own translation rule — is not visible to any of them, which is why the
/// names are written down and then asserted by <c>MetricsNamingTests</c> against a live scrape.
/// </summary>
/// <remarks>
/// The plan's hand-applied translation table (strategy §3.11) was wrong in three of its nineteen
/// business rows. The names below are the exporter's actual output on
/// <c>OpenTelemetry.Exporter.Prometheus.AspNetCore 1.18.0-beta.1</c>, verified by scraping a test
/// host: <c>aveline.process.cpu_seconds</c> gains <c>_count</c>, and both <c>ratio</c> metrics
/// (<c>aveline.api.error_rate</c>, <c>aveline.agent.success_rate</c>) gain <c>_ratio</c> rather
/// than passing through unchanged. Do not hand-edit these without re-running that test.
/// </remarks>
internal static class MetricsCatalog
{
    /// <summary>Every series this application publishes under the <c>aveline</c> prefix.</summary>
    internal static readonly IReadOnlyList<ExportedMetric> All =
    [
        // --- The nineteen bridged business metrics (gauge, dotted name = Postgres MetricName).
        BusinessCounter("aveline.process.cpu_seconds", "aveline_process_cpu_seconds_total"),
        Business("aveline.process.working_set_bytes", "aveline_process_working_set_bytes", "bytes"),
        Business("aveline.process.gc_heap_bytes", "aveline_process_gc_heap_bytes", "bytes"),
        Business("aveline.process.thread_count", "aveline_process_thread_count", "count"),
        Business("aveline.process.threadpool_queue_length", "aveline_process_threadpool_queue_length_count", "count"),
        Business("aveline.queue.telemetry_channel", "aveline_queue_telemetry_channel_count", "count"),
        BusinessCounter("aveline.api.telemetry.dropped", "aveline_api_telemetry_dropped_total"),
        BusinessCounter("aveline.eventbus.failed", "aveline_eventbus_failed_total"),
        Business("aveline.eventbus.backlog", "aveline_eventbus_backlog_count", "count"),
        Business("aveline.eventbus.publish_latency_ms", "aveline_eventbus_publish_latency_ms_milliseconds", "ms"),
        Business("aveline.api.requests_per_second", "aveline_api_requests_per_second_count", "count"),
        Business("aveline.api.error_rate", "aveline_api_error_rate_ratio", "ratio"),
        Business("aveline.api.latency_p95", "aveline_api_latency_p95_milliseconds", "ms"),
        Business("aveline.agent.runs_running", "aveline_agent_runs_running_count", "count"),
        Business("aveline.agent.success_rate", "aveline_agent_success_rate_ratio", "ratio"),
        Business("aveline.agent.paused_count", "aveline_agent_paused_count", "count"),
        Business("aveline.agent.steps_per_run", "aveline_agent_steps_per_run_count", "count"),
        // Cumulative over the retention window, so it is a counter: an `increase()` over it is
        // runs completed per interval, which is the activity view the gauges above cannot
        // give. Pruning (RunRetentionDays, default 400) makes it step down, which Prometheus
        // reads as a reset - correct, just under-reported for the one interval that spans it.
        BusinessCounter("aveline.agent.runs_total", "aveline_agent_runs_total"),
        Business("aveline.blossom.balance", "aveline_blossom_balance_count", "count"),
        Business("aveline.blossom.reconciliation.drift", "aveline_blossom_reconciliation_drift_count", "count"),
        Business("aveline.blossom.consumed_rate", "aveline_blossom_consumed_rate_count", "count"),
        // Slice 5: emitted by the collector from Npgsql's pool instruments (M-9).
        Business("aveline.db.pool.saturation", "aveline_db_pool_saturation_ratio", "ratio"),

        // --- The event-bus instruments (EventBusMetrics, meter "Aveline.Api.Eventing").
        new("aveline.events.published", "aveline_events_published_events_total", "events", ExportedMetricKind.Counter, ExportedMetricFamily.Eventing),
        new("aveline.events.received", "aveline_events_received_events_total", "events", ExportedMetricKind.Counter, ExportedMetricFamily.Eventing),
        new("aveline.events.failed", "aveline_events_failed_events_total", "events", ExportedMetricKind.Counter, ExportedMetricFamily.Eventing),
        new(
            "aveline.events.publish_latency_ms",
            "aveline_events_publish_latency_ms_milliseconds",
            "ms",
            ExportedMetricKind.Histogram,
            ExportedMetricFamily.Eventing),

        // --- The notification metric family (NotificationMetrics, Slice 7).
        Notification("aveline.notification.delivery", "aveline_notification_delivery_total", string.Empty, ExportedMetricKind.Counter),
        Notification("aveline.notification.fcm_credential_configured", "aveline_notification_fcm_credential_configured", string.Empty, ExportedMetricKind.Gauge),
        Notification("aveline.notification.inbox_backlog", "aveline_notification_inbox_backlog", string.Empty, ExportedMetricKind.Gauge),
        Notification("aveline.notification.delivery_success_rate", "aveline_notification_delivery_success_rate_ratio", "ratio", ExportedMetricKind.Gauge),
        Notification("aveline.notification.inbox_read_rate", "aveline_notification_inbox_read_rate_ratio", "ratio", ExportedMetricKind.Gauge),
        Notification("aveline.notification.dismiss_rate", "aveline_notification_dismiss_rate_ratio", "ratio", ExportedMetricKind.Gauge),
        Notification("aveline.notification.volume_by_type", "aveline_notification_volume_by_type", string.Empty, ExportedMetricKind.Gauge),
        Notification("aveline.notification.push_dispatch_failures", "aveline_notification_push_dispatch_failures", string.Empty, ExportedMetricKind.Gauge),
        Notification("aveline.notification.failure_reasons", "aveline_notification_failure_reasons", string.Empty, ExportedMetricKind.Gauge),
        Notification("aveline.notification.time_to_read_p50_minutes", "aveline_notification_time_to_read_p50_minutes", string.Empty, ExportedMetricKind.Gauge),
    ];

    /// <summary>The bridged business metrics, in the order the collector produces them.</summary>
    internal static readonly IReadOnlyList<ExportedMetric> BusinessMetrics =
        All.Where(metric => metric.Family == ExportedMetricFamily.Business).ToArray();

    /// <summary>The notification family's series (Slice 7).</summary>
    internal static readonly IReadOnlyList<ExportedMetric> NotificationMetrics =
        All.Where(metric => metric.Family == ExportedMetricFamily.Notification).ToArray();

    /// <summary>
    /// Label keys that must never appear on a Prometheus series (plan §7.3). A tenant id, a run id
    /// or a raw path creates one series per entity and eventually takes the TSDB down; a
    /// connection string is also a secret leak (R-16). Kept next to the instruments so the
    /// cardinality test and the catalogue cannot drift.
    /// </summary>
    public static readonly IReadOnlyList<string> ForbiddenLabelKeys =
    [
        "organizationId",
        "organization_id",
        "userId",
        "user_id",
        "customerId",
        "customer_id",
        "conversationId",
        "conversation_id",
        "messageId",
        "message_id",
        "workflowId",
        "workflow_id",
        "runId",
        "run_id",
        "apiKeyId",
        "api_key_id",
        "connection_string",
        "db.client.connection.pool.name",
    ];

    /// <summary>Looks a business metric up by its dotted name.</summary>
    internal static ExportedMetric? FindBusiness(string dottedName)
        => BusinessMetrics.FirstOrDefault(metric => metric.DottedName == dottedName);

    private static ExportedMetric Business(string dottedName, string prometheusName, string unit)
        => new(dottedName, prometheusName, unit, ExportedMetricKind.Gauge, ExportedMetricFamily.Business);

    /// <summary>
    /// A bridged metric whose source value is CUMULATIVE. It is exposed as an OpenTelemetry counter
    /// rather than a gauge: `rate()` on a gauge is both semantically wrong and flagged by Grafana,
    /// and a counter reset (an API restart) is something Prometheus handles correctly only when the
    /// series is typed as a counter. No unit, so the exporter produces the idiomatic
    /// <c>&lt;name&gt;_total</c> rather than <c>&lt;name&gt;_count_total</c>; the persistence path
    /// keeps unit <c>count</c> in <c>SystemMetricSamples</c>.
    /// </summary>
    private static ExportedMetric BusinessCounter(string dottedName, string prometheusName)
        => new(dottedName, prometheusName, string.Empty, ExportedMetricKind.Counter, ExportedMetricFamily.Business);

    private static ExportedMetric Notification(string dottedName, string prometheusName, string unit, ExportedMetricKind kind)
        => new(dottedName, prometheusName, unit, kind, ExportedMetricFamily.Notification);
}

/// <summary>
/// The bridged business-metric gauges. <c>SystemMetricCollector</c> publishes its snapshot here
/// <b>before</b> attempting the database write, so a database outage does not blind the operator
/// dashboard (plan §3.3).
/// </summary>
/// <remarks>
/// A gauge is recorded only when a value exists: an absent measurement produces no series, which
/// preserves BR-7.10 ("a metric whose value cannot be determined is omitted, never recorded as 0").
/// <para>
/// <b>Decimal precision decision (plan N-10b).</b> These are <see cref="double"/> gauges, so
/// <c>aveline_blossom_balance_count</c> is a float64 approximation of the <c>decimal</c> the
/// collector computes. Postgres remains the exact record for currency; the Prometheus series is
/// for dashboards and alerting only, and a consumer that needs the exact balance reads the API.
/// </para>
/// </remarks>
public sealed class AvelineMetrics : IDisposable
{
    /// <summary>The meter the gauges are created on; registered by ObservabilityConfiguration.</summary>
    public static readonly string MeterName = ObservabilityConfiguration.InstrumentationName;

    private readonly Meter _meter;
    private readonly object _gate = new();
    private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);

    public AvelineMetrics()
    {
        _meter = new Meter(MeterName);

        foreach (var metric in MetricsCatalog.BusinessMetrics)
        {
            var name = metric.DottedName;
            var description = $"Bridged business metric {name} (Postgres SystemMetricSamples).";

            // Cumulative sources are counters; everything else is a gauge.
            if (metric.Kind == ExportedMetricKind.Counter)
            {
                _meter.CreateObservableCounter(
                    name,
                    () => Observe(name),
                    unit: metric.Unit,
                    description: description);
            }
            else
            {
                _meter.CreateObservableGauge(
                    name,
                    () => Observe(name),
                    unit: metric.Unit,
                    description: description);
            }
        }
    }

    /// <summary>
    /// Replaces the published snapshot. A null value removes the series rather than recording a
    /// zero, so a gap stays a gap (BR-7.10).
    /// </summary>
    public void Publish(IReadOnlyDictionary<string, double?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        lock (_gate)
        {
            _values.Clear();
            foreach (var (key, value) in values)
            {
                if (value is not null)
                {
                    _values[key] = value.Value;
                }
            }
        }
    }

    /// <summary>Exposed for tests and diagnostics: the dotted names currently carrying a value.</summary>
    public IReadOnlyCollection<string> PublishedNames
    {
        get
        {
            lock (_gate)
            {
                return _values.Keys.ToArray();
            }
        }
    }

    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<double>> Observe(string name)
    {
        lock (_gate)
        {
            return _values.TryGetValue(name, out var value)
                ? [new Measurement<double>(value)]
                : [];
        }
    }
}

/// <summary>Registration for the bridged business-metric gauges.</summary>
public static class MetricsConfiguration
{
    public static IServiceCollection AddAvelineMetrics(this IServiceCollection services)
    {
        services.AddSingleton<AvelineMetrics>();
        return services;
    }
}
