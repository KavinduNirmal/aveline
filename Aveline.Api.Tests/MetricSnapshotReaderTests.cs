using Aveline.Api.Modules.Statistics.Domain;
using Aveline.Api.Modules.Statistics.Jobs;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 2 (plan §6.2, strategy N-10). The bridge's mapping function must reproduce
/// <c>BuildSamples</c>' *actual* behaviour, not a member-wise projection of the snapshot:
/// <c>aveline.eventbus.backlog</c> is derived from two members and exists only when both are
/// present. The plan's proposed "a fully-populated snapshot yields nineteen names" test would have
/// passed vacuously (if <c>Flatten</c> were written over the name list) or failed on exactly this
/// row; the differential assertion below is what actually pins the mapping.
/// </summary>
public class MetricSnapshotReaderTests
{
    private static MetricSnapshot MaximalSnapshot() => new()
    {
        ProcessCpuSeconds = 12.5,
        WorkingSetBytes = 1024,
        GcHeapBytes = 2048,
        ThreadCount = 8,
        ThreadPoolQueueLength = 3,
        TelemetryChannelDepth = 4,
        TelemetryDropped = 1,
        EventBusPublished = 10,
        EventBusReceived = 9,
        EventBusFailed = 1,
        EventBusPublishLatencyMs = 12.5,
        ApiRequestsPerSecond = 2.5,
        ApiErrorRate = 0.02,
        AgentRunsRunning = 3,
        BlossomBalance = 120.5m,
        BlossomReconciliationDrift = 0.25m,
        BlossomConsumedRate = 3.5,
        AgentSuccessRate = 0.9,
        AgentPausedCount = 2,
        AgentStepsPerRun = 7.5,
        ApiLatencyP95Ms = 420,
        DbPoolSaturation = 0.42,
    };

    public static TheoryData<string, MetricSnapshot> Fixtures => new()
    {
        { "empty", new MetricSnapshot() },
        { "thread-count-only", new MetricSnapshot { ThreadCount = 8 } },
        { "published-set-received-null", new MetricSnapshot { EventBusPublished = 10 } },
        { "published-null-received-set", new MetricSnapshot { EventBusReceived = 4 } },
        { "backlog-derivable", new MetricSnapshot { EventBusPublished = 10, EventBusReceived = 9 } },
        { "blossom-decimals-only", new MetricSnapshot { BlossomBalance = 120.5m, BlossomReconciliationDrift = 0.25m } },
        { "maximal", MaximalSnapshot() },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Flatten_MatchesBuildSamplesExactly(string name, MetricSnapshot snapshot)
    {
        var emitted = SystemMetricCollector.BuildSamples(snapshot)
            .Select(sample => sample.MetricName)
            .ToHashSet(StringComparer.Ordinal);

        var flattened = MetricSnapshotReader.Flatten(snapshot)
            .Where(pair => pair.Value is not null)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        flattened.Should().BeEquivalentTo(
            emitted,
            $"{name}: every persisted sample must have a gauge and every gauge a persisted sample");
    }

    [Fact]
    public void Flatten_LeavesBacklogNullWhenOnlyOneCounterIsKnown()
    {
        // The key is always present so the mapping is complete; the *emitted* subset is the
        // non-null subset, and `eventbus.backlog` must not be emitted from one counter alone.
        var emitted = MetricSnapshotReader.Flatten(new MetricSnapshot { EventBusPublished = 10 })
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        emitted.Should().NotContainKey("aveline.eventbus.backlog");
    }

    [Fact]
    public void Flatten_DerivesBacklogFromBothCounters()
    {
        MetricSnapshotReader.Flatten(new MetricSnapshot { EventBusPublished = 10, EventBusReceived = 4 })
            ["aveline.eventbus.backlog"].Should().Be(6);
    }

    [Fact]
    public void Flatten_KeepsTheDecimalMetricsAsFiniteDoubleApproximations()
    {
        // Documented divergence (N-10b): Postgres keeps the exact decimal; the Prometheus gauge is
        // a float64 approximation, so the value must at least survive the conversion.
        var flattened = MetricSnapshotReader.Flatten(new MetricSnapshot { BlossomBalance = 120.5m });

        flattened["aveline.blossom.balance"].Should().Be(120.5d);
    }

    [Fact]
    public void ProducedMetricNames_AreAllFlattenKeys()
    {
        // Three-way consistency, leg 1 (strategy N-2): the seeded-rule guard watches
        // ProducedMetricNames, so every name it can see must be producible by the reader.
        var keys = MetricSnapshotReader.Flatten(MaximalSnapshot()).Keys;

        SystemMetricCollector.ProducedMetricNames.Should().BeSubsetOf(keys);
    }

    [Fact]
    public void BuildSamples_EmitsExactlyTheNonNullFlattenKeys()
    {
        // Leg 2: the mapping cannot drift in the other direction either.
        var emitted = SystemMetricCollector.BuildSamples(MaximalSnapshot())
            .Select(sample => sample.MetricName)
            .ToHashSet(StringComparer.Ordinal);

        emitted.Should().BeSubsetOf(SystemMetricCollector.ProducedMetricNames);
        emitted.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Flatten_CoversExactlyTheProducedMetricNamesPlusBacklog()
    {
        // Leg 3: the reader's key set is the collector's name list, no more and no less. If a name
        // is added to ProducedMetricNames without a producer path, this fails.
        MetricSnapshotReader.Flatten(new MetricSnapshot()).Keys
            .Should().BeEquivalentTo(SystemMetricCollector.ProducedMetricNames);
    }
}
