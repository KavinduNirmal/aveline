using Aveline.Api.Modules.Statistics.Jobs;

namespace Aveline.Api.Modules.Statistics.Domain;

/// <summary>
/// The single name→value mapping for a <see cref="MetricSnapshot"/> (plan §3.3, strategy N-10).
/// <c>SystemMetricCollector.BuildSamples</c> persists the non-null subset to Postgres and the
/// bridged Prometheus gauges publish the same subset, so the two sinks cannot disagree about which
/// metric exists.
/// </summary>
/// <remarks>
/// This is deliberately <b>not</b> a member-wise projection of the record: <c>eventbus.backlog</c>
/// is derived from two members and is present only when both are known. It is also the reason the
/// differential test against <c>BuildSamples</c> exists rather than a completeness test over a
/// fully-populated fixture.
/// </remarks>
public static class MetricSnapshotReader
{
    /// <summary>
    /// Every dotted metric name with its value, or <c>null</c> where the host could not determine
    /// it. A null value is omitted by both sinks rather than recorded as zero (BR-7.10).
    /// </summary>
    public static IReadOnlyDictionary<string, double?> Flatten(MetricSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            ["aveline.process.cpu_seconds"] = snapshot.ProcessCpuSeconds,
            ["aveline.process.working_set_bytes"] = snapshot.WorkingSetBytes,
            ["aveline.process.gc_heap_bytes"] = snapshot.GcHeapBytes,
            ["aveline.process.thread_count"] = snapshot.ThreadCount,
            ["aveline.process.threadpool_queue_length"] = snapshot.ThreadPoolQueueLength,
            ["aveline.queue.telemetry_channel"] = snapshot.TelemetryChannelDepth,
            ["aveline.api.telemetry.dropped"] = snapshot.TelemetryDropped,
            ["aveline.eventbus.failed"] = snapshot.EventBusFailed,

            // Derived from two members: absent entirely until both counters are known.
            ["aveline.eventbus.backlog"] =
                snapshot.EventBusPublished is { } published && snapshot.EventBusReceived is { } received
                    ? published - received
                    : null,

            ["aveline.eventbus.publish_latency_ms"] = snapshot.EventBusPublishLatencyMs,

            ["aveline.api.requests_per_second"] = snapshot.ApiRequestsPerSecond,
            ["aveline.api.error_rate"] = snapshot.ApiErrorRate,
            ["aveline.api.latency_p95"] = snapshot.ApiLatencyP95Ms,
            ["aveline.agent.runs_running"] = snapshot.AgentRunsRunning,
            ["aveline.agent.success_rate"] = snapshot.AgentSuccessRate,
            ["aveline.agent.paused_count"] = snapshot.AgentPausedCount,
            ["aveline.agent.steps_per_run"] = snapshot.AgentStepsPerRun,
            ["aveline.agent.runs_total"] = snapshot.AgentRunsTotal,

            // The two currency metrics are `decimal?` so Postgres keeps them exact. The gauge is a
            // float64 approximation by design (N-10b); Postgres remains the record of truth.
            ["aveline.blossom.balance"] = (double?)snapshot.BlossomBalance,
            ["aveline.blossom.reconciliation.drift"] = (double?)snapshot.BlossomReconciliationDrift,
            ["aveline.blossom.consumed_rate"] = snapshot.BlossomConsumedRate,

            // Slice 5 (M-9): the connection-pool saturation ratio Npgsql observes but cannot expose
            // through its public statistics API.
            ["aveline.db.pool.saturation"] = snapshot.DbPoolSaturation,
        };
    }
}
