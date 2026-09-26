using System.Diagnostics;
using Aveline.Api.Common.Jobs;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Statistics.Domain;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Repositories;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Statistics.Jobs;

/// <summary>
/// A point-in-time set of candidate metric values gathered by
/// <see cref="SystemMetricCollector"/>. Every member is nullable: a metric the host cannot
/// determine is left null and therefore omitted from the persisted samples (BR-7.10).
/// </summary>
public sealed record MetricSnapshot
{
    public double? ProcessCpuSeconds { get; init; }

    public long? WorkingSetBytes { get; init; }

    public long? GcHeapBytes { get; init; }

    public int? ThreadCount { get; init; }

    public long? ThreadPoolQueueLength { get; init; }

    public long? TelemetryChannelDepth { get; init; }

    public long? TelemetryDropped { get; init; }

    public long? EventBusPublished { get; init; }

    public long? EventBusReceived { get; init; }

    public long? EventBusFailed { get; init; }

    /// <summary>
    /// Interpolated p95 publish latency in milliseconds (M-8). Null below
    /// <see cref="EventBusMetrics.MinLatencySamplesForPercentile"/>, so the metric is omitted
    /// rather than computed from one observation.
    /// </summary>
    public double? EventBusPublishLatencyMs { get; init; }

    public double? ApiRequestsPerSecond { get; init; }

    public double? ApiErrorRate { get; init; }

    public long? AgentRunsRunning { get; init; }

    /// <summary>Minimum cached Blossom balance across usage accounts; negative when overdrawn.</summary>
    public decimal? BlossomBalance { get; init; }

    /// <summary>Maximum absolute difference between the cached and ledger-derived balance.</summary>
    public decimal? BlossomReconciliationDrift { get; init; }

    /// <summary>Blossoms consumed per minute over the last hour.</summary>
    public double? BlossomConsumedRate { get; init; }

    /// <summary>Terminal agent runs that succeeded, over the last hour.</summary>
    public double? AgentSuccessRate { get; init; }

    /// <summary>Agent runs currently paused for approval.</summary>
    public long? AgentPausedCount { get; init; }

    /// <summary>
    /// Terminal agent runs, cumulative over the retained window. A counter, not a window: it is
    /// what a rate or an increase is computed over, and the only agent metric that answers "is the
    /// agent being used at all?" - the running and paused counts are instantaneous and read zero
    /// between runs, however much work the agent is doing.
    /// </summary>
    /// <remarks>
    /// Runs are pruned after <c>AgentStats:RunRetentionDays</c> (default 400), so this is monotonic
    /// only within that window: it steps <em>down</em> when a day of runs ages out. That is a
    /// counter reset as far as Prometheus is concerned, and <c>increase()</c> handles one by
    /// restarting its accumulation - the interval spanning the prune under-reports, and no interval
    /// reports a spike that did not happen. Preferred over an in-process counter because the value
    /// then survives an API restart.
    /// </remarks>
    public long? AgentRunsTotal { get; init; }

    /// <summary>Mean step count of agent runs started in the last hour.</summary>
    public double? AgentStepsPerRun { get; init; }

    /// <summary>Bucket-interpolated p95 API latency over the current hour, in milliseconds.</summary>
    public double? ApiLatencyP95Ms { get; init; }

    /// <summary>
    /// Worst connection-pool saturation across the process, <c>used / max</c> (Slice 5, M-9).
    /// Read from Npgsql's instruments through <c>NpgsqlPoolMetricsListener</c>, not from the
    /// (internal) Npgsql statistics API.
    /// </summary>
    public double? DbPoolSaturation { get; init; }
}

/// <summary>
/// Samples process, queue, event-bus and throughput metrics into
/// <c>SystemMetricSamples</c> every <c>Observability:SystemMetricCollectionSeconds</c>
/// (default 30 s) (FR-7.5–FR-7.10, S-35, S-36, S-40, S-41). The job never crashes the host:
/// a failed database write buffers at most <see cref="MaxBufferedSamples"/> samples in memory
/// and flushes them on the next successful pass.
/// </summary>
public class SystemMetricCollector(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<SystemMetricCollector> logger,
    IConfiguration configuration,
    AvelineMetrics? metrics = null)
    : StatisticsJobBase(scopeFactory, jobLock, logger)
{
    /// <summary>Upper bound on the in-memory buffer that survives a database outage.</summary>
    public const int MaxBufferedSamples = 100;

    /// <summary>
    /// Every metric name <see cref="BuildSamples"/> can emit. The seeded alert rules must
    /// reference only these names; a unit test compares the two so a rule cannot drift onto a
    /// metric nothing produces (C-5).
    /// </summary>
    public static readonly IReadOnlyList<string> ProducedMetricNames =
    [
        "aveline.process.cpu_seconds",
        "aveline.process.working_set_bytes",
        "aveline.process.gc_heap_bytes",
        "aveline.process.thread_count",
        "aveline.process.threadpool_queue_length",
        "aveline.queue.telemetry_channel",
        "aveline.api.telemetry.dropped",
        "aveline.eventbus.failed",
        "aveline.eventbus.backlog",
        "aveline.eventbus.publish_latency_ms",
        "aveline.api.requests_per_second",
        "aveline.api.error_rate",
        "aveline.api.latency_p95",
        "aveline.agent.runs_running",
        "aveline.agent.success_rate",
        "aveline.agent.paused_count",
        "aveline.agent.steps_per_run",
        "aveline.agent.runs_total",
        "aveline.blossom.balance",
        "aveline.blossom.reconciliation.drift",
        "aveline.blossom.consumed_rate",
        "aveline.db.pool.saturation",
    ];

    private readonly List<SystemMetricSample> _buffer = [];
    private readonly int _collectionSeconds =
        Math.Max(1, configuration.GetValue("Observability:SystemMetricCollectionSeconds", 30));

    private long _droppedSamples;
    private long _databaseReadFailures;

    protected override string JobName => "system-metric-collector";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_collectionSeconds);

    /// <summary>Samples dropped because the in-memory buffer was already full.</summary>
    public long DroppedSamples => Interlocked.Read(ref _droppedSamples);

    /// <summary>
    /// Database capture passes that threw, so their database-derived metrics are missing.
    /// A non-zero value means the alert evaluator is seeing gaps, not health (§3.3(c)).
    /// </summary>
    public long DatabaseReadFailures => Interlocked.Read(ref _databaseReadFailures);

    /// <summary>Exposed for tests: samples currently held for the next successful flush.</summary>
    internal int BufferedSampleCount => _buffer.Count;

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var snapshot = await CaptureAsync(scope.ServiceProvider, cancellationToken);
        var samples = BuildSamples(snapshot);

        // Publish the operator-facing gauges BEFORE the database write (plan §3.3, D1): a database
        // outage must not blind the dashboard, and the persistence path is untouched by design.
        PublishToMetrics(snapshot);

        // Anything still buffered from an earlier failure is written together with this pass.
        var pending = new List<SystemMetricSample>(_buffer);
        pending.AddRange(samples);

        var repository = scope.ServiceProvider.GetRequiredService<ISystemMetricRepository>();
        try
        {
            var written = await repository.UpsertAsync(pending, cancellationToken);
            _buffer.Clear();
            return written;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "System metric write failed; buffering {Count} of {Buffered} samples.",
                samples.Count, _buffer.Count);

            foreach (var sample in samples)
            {
                if (_buffer.Count >= MaxBufferedSamples)
                {
                    Interlocked.Increment(ref _droppedSamples);
                    continue;
                }

                _buffer.Add(sample);
            }

            return 0;
        }
    }

    /// <summary>
    /// Publishes the snapshot to the bridged Prometheus gauges. Split out so a test can drive one
    /// pass without a database, and so the ordering against the persistence path is explicit.
    /// </summary>
    public void PublishToMetrics(MetricSnapshot snapshot)
        => metrics?.Publish(MetricSnapshotReader.Flatten(snapshot));

    /// <summary>
    /// Pure sample construction so the mapping can be unit-tested without a host. A null
    /// member yields no sample at all; nothing is ever recorded as zero by default (BR-7.10).
    /// </summary>
    public static IReadOnlyList<SystemMetricSample> BuildSamples(MetricSnapshot snapshot)
    {
        const string dimensionsJson = "{}";
        var dimensionHash = MetricDimensionHasher.Hash(dimensionsJson);
        var sampledAt = DateTime.UtcNow;
        var samples = new List<SystemMetricSample>();

        void AddDecimal(string metricName, double? value, string unit)
        {
            if (value is null)
            {
                return;
            }

            samples.Add(new SystemMetricSample
            {
                MetricName = metricName,
                DimensionsJson = dimensionsJson,
                DimensionHash = dimensionHash,
                ValueDecimal = (decimal)value.Value,
                Unit = unit,
                WindowStart = sampledAt,
                WindowSize = "instant",
                SampledAt = sampledAt,
            });
        }

        void AddDecimalExact(string metricName, decimal? value, string unit)
        {
            if (value is null)
            {
                return;
            }

            samples.Add(new SystemMetricSample
            {
                MetricName = metricName,
                DimensionsJson = dimensionsJson,
                DimensionHash = dimensionHash,
                ValueDecimal = value.Value,
                Unit = unit,
                WindowStart = sampledAt,
                WindowSize = "instant",
                SampledAt = sampledAt,
            });
        }

        void AddBigint(string metricName, long? value, string unit)
        {
            if (value is null)
            {
                return;
            }

            samples.Add(new SystemMetricSample
            {
                MetricName = metricName,
                DimensionsJson = dimensionsJson,
                DimensionHash = dimensionHash,
                ValueBigint = value.Value,
                Unit = unit,
                WindowStart = sampledAt,
                WindowSize = "instant",
                SampledAt = sampledAt,
            });
        }

        AddDecimal("aveline.process.cpu_seconds", snapshot.ProcessCpuSeconds, "count");
        AddBigint("aveline.process.working_set_bytes", snapshot.WorkingSetBytes, "bytes");
        AddBigint("aveline.process.gc_heap_bytes", snapshot.GcHeapBytes, "bytes");
        AddBigint("aveline.process.thread_count", snapshot.ThreadCount, "count");
        AddBigint("aveline.process.threadpool_queue_length", snapshot.ThreadPoolQueueLength, "count");
        AddBigint("aveline.queue.telemetry_channel", snapshot.TelemetryChannelDepth, "count");
        AddBigint("aveline.api.telemetry.dropped", snapshot.TelemetryDropped, "count");
        AddBigint("aveline.eventbus.failed", snapshot.EventBusFailed, "count");
        AddDecimal("aveline.eventbus.publish_latency_ms", snapshot.EventBusPublishLatencyMs, "ms");
        AddBigint(
            "aveline.eventbus.backlog",
            snapshot.EventBusPublished is { } published && snapshot.EventBusReceived is { } received
                ? published - received
                : null,
            "count");
        AddDecimal("aveline.api.requests_per_second", snapshot.ApiRequestsPerSecond, "count");
        AddDecimal("aveline.api.error_rate", snapshot.ApiErrorRate, "ratio");
        AddDecimal("aveline.api.latency_p95", snapshot.ApiLatencyP95Ms, "ms");
        AddBigint("aveline.agent.runs_running", snapshot.AgentRunsRunning, "count");
        AddDecimal("aveline.agent.success_rate", snapshot.AgentSuccessRate, "ratio");
        AddBigint("aveline.agent.paused_count", snapshot.AgentPausedCount, "count");
        AddDecimal("aveline.agent.steps_per_run", snapshot.AgentStepsPerRun, "count");
        AddBigint("aveline.agent.runs_total", snapshot.AgentRunsTotal, "count");
        AddDecimalExact("aveline.blossom.balance", snapshot.BlossomBalance, "count");
        AddDecimalExact("aveline.blossom.reconciliation.drift", snapshot.BlossomReconciliationDrift, "count");
        AddDecimal("aveline.blossom.consumed_rate", snapshot.BlossomConsumedRate, "count");
        AddDecimal("aveline.db.pool.saturation", snapshot.DbPoolSaturation, "ratio");

        return samples;
    }

    /// <summary>
    /// Gathers one snapshot. Virtual so a test can supply a deterministic snapshot without a
    /// running host.
    /// </summary>
    protected virtual async Task<MetricSnapshot> CaptureAsync(
        IServiceProvider services, CancellationToken cancellationToken)
    {
        var snapshot = new MetricSnapshot();

        try
        {
            using var process = Process.GetCurrentProcess();
            snapshot = snapshot with
            {
                ProcessCpuSeconds = process.TotalProcessorTime.TotalSeconds,
                WorkingSetBytes = process.WorkingSet64,
                ThreadCount = process.Threads.Count,
                ThreadPoolQueueLength = ThreadPool.PendingWorkItemCount,
                GcHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A host that refuses a process counter simply omits that metric (BR-7.10).
        }

        if (services.GetService<TelemetryChannel>() is { } channel)
        {
            snapshot = snapshot with
            {
                TelemetryChannelDepth = channel.PendingCount,
                TelemetryDropped = channel.DroppedSamples,
            };
        }

        if (services.GetService<EventBusMetrics>() is { } eventBus)
        {
            var counters = eventBus.Snapshot();
            snapshot = snapshot with
            {
                EventBusPublished = counters.GetValueOrDefault("aveline.events.published"),
                EventBusReceived = counters.GetValueOrDefault("aveline.events.received"),
                EventBusFailed = counters.GetValueOrDefault("aveline.events.failed"),
                EventBusPublishLatencyMs = eventBus.LatencyP95Ms,
            };
        }

        // Slice 5 (M-9): Npgsql is the source, the collector is the emitter. The listener reports
        // nothing until a pool has been observed, so the metric is omitted rather than zeroed.
        if (services.GetService<NpgsqlPoolMetricsListener>() is { } pool
            && pool.TryGetSaturation(out var saturation))
        {
            snapshot = snapshot with { DbPoolSaturation = saturation };
        }

        if (services.GetService<AppDbContext>() is { } db)
        {
            await CaptureDatabaseAsync(db, snapshot, value => snapshot = value, cancellationToken);
        }

        return snapshot;
    }

    private async Task CaptureDatabaseAsync(
        AppDbContext db,
        MetricSnapshot snapshot,
        Action<MetricSnapshot> update,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);

            var rows = await db.ApiRequestMetrics
                .AsNoTracking()
                .Where(metric => metric.WindowSize == "hour" && metric.WindowStart == hourStart)
                .Select(metric => new { metric.RequestCount, metric.ErrorCount, metric.BucketCounts })
                .ToListAsync(cancellationToken);

            var requests = rows.Sum(row => row.RequestCount);
            if (requests > 0)
            {
                var elapsedSeconds = Math.Max(1d, (now - hourStart).TotalSeconds);
                snapshot = snapshot with
                {
                    ApiRequestsPerSecond = requests / elapsedSeconds,
                    ApiErrorRate = (double)rows.Sum(row => row.ErrorCount) / requests,
                };

                // The percentile is interpolated from the cumulative latency buckets (S-26).
                var buckets = new int[ApiRequestMetric.BucketCount];
                foreach (var row in rows)
                {
                    for (var i = 0; i < buckets.Length && i < row.BucketCounts.Length; i++)
                    {
                        buckets[i] += row.BucketCounts[i];
                    }
                }

                snapshot = snapshot with
                {
                    ApiLatencyP95Ms = LatencyBuckets.Compute(buckets, requests, minSamples: 1).P95Ms,
                };
            }

            snapshot = snapshot with
            {
                // Cumulative, and deliberately a full count: the running and paused counts beside
                // it already scan this table each pass, so this adds no new kind of cost. If the
                // table ever grows enough to matter, the daily rollup is the bounded source.
                AgentRunsTotal = await db.AgentWorkflowRuns
                    .AsNoTracking()
                    .LongCountAsync(
                        run => run.Status == AgentRunStatus.Succeeded
                               || run.Status == AgentRunStatus.Failed
                               || run.Status == AgentRunStatus.Cancelled
                               || run.Status == AgentRunStatus.TimedOut,
                        cancellationToken),
                AgentRunsRunning = await db.AgentWorkflowRuns
                    .AsNoTracking()
                    .LongCountAsync(run => run.Status == AgentRunStatus.Running, cancellationToken),
                AgentPausedCount = await db.AgentWorkflowRuns
                    .AsNoTracking()
                    .LongCountAsync(run => run.Status == AgentRunStatus.PausedForApproval, cancellationToken),
            };

            snapshot = await CaptureAgentRunsAsync(db, snapshot, now, cancellationToken);
            snapshot = await CaptureBlossomAsync(db, snapshot, now, cancellationToken);

            update(snapshot);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A transient read failure omits the database metrics for this pass (BR-7.10),
            // but it must be visible: a silent drop makes a missing drift sample look like a
            // healthy one and lets a firing integrity alert auto-resolve (§3.3(c)).
            Interlocked.Increment(ref _databaseReadFailures);
            logger.LogError(
                exception,
                "System metric database capture failed; the database-derived metrics are "
                + "omitted for this pass (failures={Failures}).",
                DatabaseReadFailures);
        }
    }

    /// <summary>Success rate and mean steps per run over the last hour (S-40).</summary>
    private static async Task<MetricSnapshot> CaptureAgentRunsAsync(
        AppDbContext db, MetricSnapshot snapshot, DateTime now, CancellationToken cancellationToken)
    {
        var windowStart = now.AddHours(-1);

        var terminalRuns = await db.AgentWorkflowRuns
            .AsNoTracking()
            .Where(run => run.StartedAt >= windowStart)
            .Where(run => run.Status == AgentRunStatus.Succeeded
                          || run.Status == AgentRunStatus.Failed
                          || run.Status == AgentRunStatus.Cancelled
                          || run.Status == AgentRunStatus.TimedOut)
            .Select(run => run.Status)
            .ToListAsync(cancellationToken);

        if (terminalRuns.Count > 0)
        {
            snapshot = snapshot with
            {
                AgentSuccessRate =
                    (double)terminalRuns.Count(status => status == AgentRunStatus.Succeeded) / terminalRuns.Count,
            };
        }

        var steps = await db.AgentWorkflowRuns
            .AsNoTracking()
            .Where(run => run.StartedAt >= windowStart)
            .Select(run => run.StepCount)
            .ToListAsync(cancellationToken);

        if (steps.Count > 0)
        {
            snapshot = snapshot with { AgentStepsPerRun = steps.Average() };
        }

        return snapshot;
    }

    /// <summary>
    /// Blossom balance, ledger/projection drift and consumption rate (S-30). Drift reuses
    /// <see cref="BlossomService.ReconciliationDrift"/> so the alert and the statement agree.
    /// Only current, open usage accounts are considered: closed historical periods are never
    /// deleted, so an old overdrawn period would otherwise pin <c>Min(...)</c> negative and
    /// latch <c>blossom.balance.negative</c> for ever (§3.3(b)).
    /// </summary>
    private static async Task<MetricSnapshot> CaptureBlossomAsync(
        AppDbContext db, MetricSnapshot snapshot, DateTime now, CancellationToken cancellationToken)
    {
        var accounts = await db.UsageAccounts
            .AsNoTracking()
            .Where(account => !account.IsClosed
                              && account.PeriodStart <= now
                              && account.PeriodEnd > now)
            .ToListAsync(cancellationToken);

        if (accounts.Count > 0)
        {
            // Scope the ledger aggregate to the open accounts so the scan stays bounded and
            // closed periods cannot contribute to the drift maximum.
            var accountIds = accounts.Select(account => account.Id).ToList();
            var ledgerSums = await db.BlossomLedgerEntries
                .AsNoTracking()
                .Where(entry => entry.EntryType != BlossomLedgerEntryType.PeriodAllocation)
                .Where(entry => accountIds.Contains(entry.UsageAccountId))
                .GroupBy(entry => entry.UsageAccountId)
                .Select(group => new { UsageAccountId = group.Key, Total = group.Sum(entry => entry.BlossomDelta) })
                .ToDictionaryAsync(row => row.UsageAccountId, row => row.Total, cancellationToken);

            snapshot = snapshot with
            {
                BlossomBalance = accounts.Min(account => account.BlossomRemaining),
                BlossomReconciliationDrift = accounts.Max(account => Math.Abs(
                    BlossomService.ReconciliationDrift(
                        account, ledgerSums.GetValueOrDefault(account.Id)))),
            };
        }

        var consumed = await db.AiUsageRecords
            .AsNoTracking()
            .Where(record => record.CreatedAt >= now.AddHours(-1))
            .SumAsync(record => (decimal?)record.BlossomUnits, cancellationToken);

        if (consumed is > 0m)
        {
            snapshot = snapshot with { BlossomConsumedRate = (double)(consumed.Value / 60m) };
        }

        return snapshot;
    }
}
