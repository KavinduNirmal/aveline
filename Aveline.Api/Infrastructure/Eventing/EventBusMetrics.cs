using System.Diagnostics.Metrics;

namespace Aveline.Api.Infrastructure.Eventing;

/// <summary>
/// Instruments the Redis event bus with <see cref="System.Diagnostics.Metrics"/> counters
/// and tracks running totals. <see cref="EventingMetricsExporter"/> periodically reads
/// <see cref="Snapshot"/> and logs it as structured JSON so the metrics flow through the
/// existing ELK / Application Insights pipeline without a separate metrics infrastructure
/// (ADR-014). The <see cref="Meter"/> counters remain available for future OpenTelemetry /
/// <c>dotnet-counters</c> integration.
/// </summary>
public sealed class EventBusMetrics
{
    /// <summary>
    /// The meter these instruments are created on. Registered by
    /// <c>ObservabilityConfiguration.AddAvelineObservability</c>; before that registration
    /// existed all four instruments were produced and silently dropped (M-1).
    /// </summary>
    public const string MeterName = Configurations.ObservabilityConfiguration.EventingMeterName;

    private readonly Counter<long> _published;
    private readonly Counter<long> _received;
    private readonly Counter<long> _failed;
    private readonly Histogram<double> _publishLatencyMs;

    /// <summary>
    /// Minimum samples before the publish-latency percentile is reported. Below it the metric is
    /// omitted rather than computed from one observation (BR-7.10 and the repo's percentile rule).
    /// </summary>
    public const int MinLatencySamplesForPercentile = 5;

    private const int LatencyWindow = 256;

    private readonly object _latencyGate = new();
    private readonly Queue<double> _latencySamples = new(LatencyWindow);

    private long _publishedTotal;
    private long _receivedTotal;
    private long _failedTotal;

    public EventBusMetrics()
    {
        var meter = new Meter(MeterName);
        _published = meter.CreateCounter<long>("aveline.events.published", "events", "Events published to the bus.");
        _received = meter.CreateCounter<long>("aveline.events.received", "events", "Events received from the bus.");
        _failed = meter.CreateCounter<long>("aveline.events.failed", "events", "Events that failed to publish or dispatch.");
        _publishLatencyMs = meter.CreateHistogram<double>("aveline.events.publish_latency_ms", "ms", "Publish latency.");
    }

    public void RecordPublished(string eventType)
    {
        Interlocked.Increment(ref _publishedTotal);
        _published.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
    }

    public void RecordReceived(string eventType)
    {
        Interlocked.Increment(ref _receivedTotal);
        _received.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
    }

    public void RecordFailed(string eventType)
    {
        Interlocked.Increment(ref _failedTotal);
        _failed.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
    }

    public void RecordPublishLatency(double milliseconds, string eventType)
    {
        _publishLatencyMs.Record(milliseconds, new KeyValuePair<string, object?>("event_type", eventType));

        // M-8 persist half: the collector samples this so `aveline.eventbus.publish_latency_ms`
        // stops being permanently omitted. Bounded so a busy bus cannot grow the buffer.
        lock (_latencyGate)
        {
            if (_latencySamples.Count >= LatencyWindow)
            {
                _latencySamples.Dequeue();
            }

            _latencySamples.Enqueue(milliseconds);
        }
    }

    /// <summary>
    /// Interpolated p95 of the recent publish latencies, or <c>null</c> below
    /// <see cref="MinLatencySamplesForPercentile"/> so the metric is omitted rather than guessed.
    /// </summary>
    public double? LatencyP95Ms
    {
        get
        {
            double[] samples;
            lock (_latencyGate)
            {
                if (_latencySamples.Count < MinLatencySamplesForPercentile)
                {
                    return null;
                }

                samples = _latencySamples.ToArray();
            }

            Array.Sort(samples);
            var rank = 0.95 * (samples.Length - 1);
            var lower = (int)Math.Floor(rank);
            var upper = (int)Math.Ceiling(rank);
            return lower == upper
                ? samples[lower]
                : samples[lower] + (samples[upper] - samples[lower]) * (rank - lower);
        }
    }

    /// <summary>
    /// A snapshot of the cumulative event counters, keyed by instrument name. Empty until at
    /// least one event has been recorded.
    /// </summary>
    public IReadOnlyDictionary<string, long> Snapshot()
    {
        var snapshot = new Dictionary<string, long>
        {
            ["aveline.events.published"] = Interlocked.Read(ref _publishedTotal),
            ["aveline.events.received"] = Interlocked.Read(ref _receivedTotal),
            ["aveline.events.failed"] = Interlocked.Read(ref _failedTotal),
        };
        return snapshot;
    }
}
