using System.Diagnostics.Metrics;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Domain;
using Aveline.Api.Modules.Statistics.Jobs;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 5 (M-9, D8). Npgsql 10.0.3 emits its eleven <c>db.client.*</c> instruments on a meter
/// named exactly <c>Npgsql</c> and they are on by default, but the meter was never registered, so
/// every one of them was produced and discarded. <c>NpgsqlDataSource.Statistics</c> is internal on
/// the pinned version, so the collector reads the instruments through a <c>MeterListener</c> — the
/// instrument is the source, the collector is the emitter, and the seeded-rule guard still holds.
/// </summary>
public class NpgsqlPoolMetricsTests
{
    [Fact]
    public void TheNpgsqlMeterIsRegisteredUnderItsRealName()
    {
        ObservabilityConfiguration.NpgsqlMeterName.Should().Be("Npgsql");
    }

    [Fact]
    public void ThePoolNameLabelIsNeutralisedRatherThanTheConnectionString()
    {
        // S-11/R-16: the label defaults to the connection string minus the password.
        DatabaseConfiguration.PoolName.Should().Be("aveline");
    }

    [Fact]
    public void TryGetSaturation_ComputesUsedOverMax()
    {
        // A private meter name keeps the observation isolated: MeterListener works against a
        // process-global registry, so the real "Npgsql" meter would see every other pool in the
        // test process.
        var meterName = UniqueMeterName();
        using var listener = new NpgsqlPoolMetricsListener(meterName);
        using var meter = new Meter(meterName);

        var count = meter.CreateObservableUpDownCounter<long>(
            "db.client.connection.count",
            () => new[]
            {
                new Measurement<long>(
                    3,
                    new KeyValuePair<string, object?>("db.client.connection.state", "used"),
                    new KeyValuePair<string, object?>("db.client.connection.pool.name", DatabaseConfiguration.PoolName)),
                new Measurement<long>(
                    7,
                    new KeyValuePair<string, object?>("db.client.connection.state", "idle"),
                    new KeyValuePair<string, object?>("db.client.connection.pool.name", DatabaseConfiguration.PoolName)),
            });

        var max = meter.CreateObservableUpDownCounter<long>(
            "db.client.connection.max",
            () => new[]
            {
                new Measurement<long>(
                    10,
                    new KeyValuePair<string, object?>("db.client.connection.pool.name", DatabaseConfiguration.PoolName)),
            });

        GC.KeepAlive(count);
        GC.KeepAlive(max);

        listener.TryGetSaturation(out var saturation).Should().BeTrue();
        saturation.Should().BeApproximately(0.3, 0.0001);
    }

    [Fact]
    public void TryGetSaturation_ReportsNothingBeforeAnyPoolExists()
    {
        // Observable instruments report only for pools that exist and have been observed, so the
        // honest answer before traffic is "unknown", which omits the metric rather than zeroing it.
        using var listener = new NpgsqlPoolMetricsListener(UniqueMeterName());

        listener.TryGetSaturation(out _).Should().BeFalse();
    }

    /// <summary>
    /// Regression test for the bug that shipped and was caught live: Npgsql's pool instruments are
    /// <c>ObservableUpDownCounter&lt;T&gt;</c> and <c>T</c> is not <c>long</c>. A listener that only
    /// registered a <c>long</c> callback captured nothing, so 32 <c>db_client_*</c> series appeared
    /// in the scrape while <c>aveline_db_pool_saturation_ratio</c> was silently absent.
    /// </summary>
    [Fact]
    public void TryGetSaturation_CapturesInstrumentsWhoseNumericTypeIsNotLong()
    {
        var meterName = UniqueMeterName();
        using var listener = new NpgsqlPoolMetricsListener(meterName);
        using var meter = new Meter(meterName);

        var max = meter.CreateObservableUpDownCounter<int>(
            "db.client.connection.max",
            () => new[] { new Measurement<int>(10, new KeyValuePair<string, object?>("db.client.connection.pool.name", "p")) });
        var used = meter.CreateObservableUpDownCounter<int>(
            "db.client.connection.count",
            () => new[]
            {
                new Measurement<int>(
                    3,
                    new KeyValuePair<string, object?>("db.client.connection.state", "used"),
                    new KeyValuePair<string, object?>("db.client.connection.pool.name", "p")),
            });

        GC.KeepAlive(max);
        GC.KeepAlive(used);

        listener.TryGetSaturation(out var saturation).Should().BeTrue();
        saturation.Should().BeApproximately(0.3, 0.0001);
    }

    private static string UniqueMeterName() => "npgsql-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void BuildSamples_EmitsTheSaturationRatioWhenItIsKnown()
    {
        var samples = SystemMetricCollector.BuildSamples(new MetricSnapshot { DbPoolSaturation = 0.75 });

        var sample = samples.Should().ContainSingle().Subject;
        sample.MetricName.Should().Be("aveline.db.pool.saturation");
        sample.ValueDecimal.Should().Be(0.75m);
    }

    [Fact]
    public void BuildSamples_OmitsTheSaturationRatioWhenItIsUnknown()
    {
        SystemMetricCollector.BuildSamples(new MetricSnapshot()).Should().BeEmpty();
    }

    [Fact]
    public void Flatten_AndTheCatalogBothCarryTheSaturationMetric()
    {
        MetricSnapshotReader.Flatten(new MetricSnapshot { DbPoolSaturation = 0.75 })
            .Should().ContainKey("aveline.db.pool.saturation");

        SystemMetricCollector.ProducedMetricNames.Should().Contain("aveline.db.pool.saturation");

        MetricsCatalog.FindBusiness("aveline.db.pool.saturation")!
            .PrometheusName.Should().Be("aveline_db_pool_saturation_ratio");
    }
}
