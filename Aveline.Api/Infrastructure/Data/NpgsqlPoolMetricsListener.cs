using System.Diagnostics.Metrics;
using Aveline.Api.Configurations;

namespace Aveline.Api.Infrastructure.Data;

/// <summary>
/// Reads Npgsql's connection-pool instruments out of the <c>Npgsql</c> meter (Slice 5, M-9).
/// </summary>
/// <remarks>
/// <para>
/// Npgsql 10.0.3 emits eleven <c>db.client.*</c> instruments on a meter named exactly
/// <c>Npgsql</c> and they are on by default, but <c>NpgsqlDataSource.Statistics</c> is
/// <c>internal</c> on the pinned version. A <see cref="MeterListener"/> is therefore the only
/// public path to the pool counts, and attaching one also causes the observable instruments to be
/// pulled — which is why the listener, not the collector, owns it.
/// </para>
/// <para>
/// The Npgsql instrument is the <i>source</i>; the collector is the <i>emitter</i>. The seeded
/// <c>db.pool.saturated</c> rule watches <c>SystemMetricCollector.ProducedMetricNames</c>, so the
/// ratio has to become a produced metric with a producer path or the guard and the rule disagree.
/// </para>
/// <para>
/// The pool-name tag is used only as a key. Before Slice 5 that tag defaulted to the connection
/// string minus the password (<c>DatabaseConfiguration.PoolName</c> now sets it explicitly), so
/// this dictionary would otherwise grow with a secret-bearing key.
/// </para>
/// </remarks>
public sealed class NpgsqlPoolMetricsListener : IDisposable
{
    private const string ConnectionCountInstrument = "db.client.connection.count";
    private const string ConnectionMaxInstrument = "db.client.connection.max";
    private const string StateTag = "db.client.connection.state";
    private const string PoolNameTag = "db.client.connection.pool.name";

    private readonly string _meterName;
    private readonly MeterListener _listener;
    private readonly object _gate = new();
    private readonly Dictionary<string, double> _usedByPool = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _maxByPool = new(StringComparer.Ordinal);

    /// <summary>The meter name production observes.</summary>
    internal const string DefaultMeterName = ObservabilityConfiguration.NpgsqlMeterName;

    /// <summary>
    /// Creates the listener for the production Npgsql meter. The meter name is injectable so a test
    /// can observe a private meter: <c>MeterListener</c> works against a process-global registry, so
    /// an unfiltered test would see every other pool in the test process.
    /// </summary>
    public NpgsqlPoolMetricsListener()
        : this(DefaultMeterName)
    {
    }

    internal NpgsqlPoolMetricsListener(string meterName)
    {
        _meterName = meterName;
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (string.Equals(instrument.Meter.Name, _meterName, StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };

        // Npgsql's instruments are ObservableUpDownCounter<T> and the numeric type it picks is an
        // implementation detail of the library (it has changed between majors). Registering the
        // numeric widths OTel can emit keeps the listener from silently capturing nothing when the
        // library's T is not `long` — which is exactly the failure this listener had when it only
        // handled `long`: 32 db_client_* series in the scrape and an absent saturation gauge.
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => OnMeasurement(instrument, value, tags));
        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => OnMeasurement(instrument, value, tags));
        _listener.SetMeasurementEventCallback<short>((instrument, value, tags, _) => OnMeasurement(instrument, value, tags));
        _listener.SetMeasurementEventCallback<byte>((instrument, value, tags, _) => OnMeasurement(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => OnMeasurement(instrument, value, tags));
        _listener.SetMeasurementEventCallback<float>((instrument, value, tags, _) => OnMeasurement(instrument, value, tags));
        _listener.SetMeasurementEventCallback<decimal>((instrument, value, tags, _) => OnMeasurement(instrument, (double)value, tags));

        _listener.Start();
    }

    /// <summary>
    /// The worst pool saturation in the process: <c>used / max</c> across every pool Npgsql has
    /// reported. Returns <c>false</c> when no pool has been observed, so the caller omits the metric
    /// rather than recording a zero (BR-7.10).
    /// </summary>
    public bool TryGetSaturation(out double saturation)
    {
        try
        {
            // Observable instruments are pulled, not pushed: refresh before reading.
            _listener.RecordObservableInstruments();
        }
        catch (Exception)
        {
            // A throwing library callback must not take the collector pass down; the metric is
            // simply omitted for this pass.
            saturation = 0;
            return false;
        }

        lock (_gate)
        {
            saturation = 0;
            var observed = false;

            foreach (var (pool, max) in _maxByPool)
            {
                if (max <= 0)
                {
                    continue;
                }

                var used = _usedByPool.GetValueOrDefault(pool);
                saturation = Math.Max(saturation, used / max);
                observed = true;
            }

            return observed;
        }
    }

    /// <summary>
    /// The pool-name tag values observed so far. Exposed so a test (and an operator) can assert the
    /// label is a bounded identifier rather than the connection string (S-11/R-16).
    /// </summary>
    public IReadOnlyCollection<string> PoolNames
    {
        get
        {
            try
            {
                _listener.RecordObservableInstruments();
            }
            catch (Exception)
            {
                // Ignored: the collector treats an inaccessible pool the same way (BR-7.10).
            }

            lock (_gate)
            {
                return _maxByPool.Keys.ToArray();
            }
        }
    }

    public void Dispose() => _listener.Dispose();

    private void OnMeasurement(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var pool = TagValue(tags, PoolNameTag) ?? "unknown";

        switch (instrument.Name)
        {
            case ConnectionCountInstrument:
                if (string.Equals(TagValue(tags, StateTag), "used", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_gate)
                    {
                        _usedByPool[pool] = measurement;
                    }
                }

                break;

            case ConnectionMaxInstrument:
                lock (_gate)
                {
                    _maxByPool[pool] = measurement;
                }

                break;
        }
    }

    private static string? TagValue(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (string.Equals(tag.Key, key, StringComparison.Ordinal))
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }
}
