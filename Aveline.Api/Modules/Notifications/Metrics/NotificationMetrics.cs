using System.Diagnostics.Metrics;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Metrics;

/// <summary>
/// The notification metric family's instruments (Slice 7, plan §6.5). Created on the
/// <c>Aveline.Api</c> meter, which <c>ObservabilityConfiguration</c> registers, so every series
/// reaches the scrape.
/// </summary>
/// <remarks>
/// <para>
/// Series names are the exporter's translated names, not the dotted instrument names: an unnamed
/// unit appends nothing and a counter gains <c>_total</c>, while a <c>ratio</c> unit appends
/// <c>_ratio</c> (verified against the pinned exporter by <c>MetricsNamingTests</c>).
/// </para>
/// <para>
/// The HTTP routes for this family are deliberately deferred (plan §5.2): exposing them requires
/// the catalog <c>S-n</c> allocation, the OpenAPI paths and the API-catalogue section in the same
/// commit. These metrics are emitted for Grafana only.
/// </para>
/// <para>
/// <b>Recorded deviation.</b> Only <c>delivery</c> is a counter, and it is incremented by the
/// dispatcher. The other three aggregating series in the plan (<c>failure_reasons</c>,
/// <c>volume_by_type</c>, <c>push_dispatch_failures</c>) are exposed as <b>windowed gauges</b>
/// rather than counters: deriving a monotonic Prometheus counter from a table needs delta
/// bookkeeping across collector passes, and a counter that double-counts or resets is worse than an
/// honest gauge. <c>docs/backend/observability.md</c> records the deviation.
/// </para>
/// </remarks>
public sealed class NotificationMetrics : IDisposable
{
    /// <summary>The meter every notification instrument is created on.</summary>
    public static readonly string MeterName = ObservabilityConfiguration.InstrumentationName;

    private readonly Meter _meter;
    private readonly Counter<long> _deliveries;
    private readonly object _gate = new();

    private readonly Dictionary<string, double> _successRate = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _readRate = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _dismissRate = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _volumeByType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _pushFailuresByPlatform = new(StringComparer.Ordinal);
    private readonly List<(string Channel, string ErrorClass, long Count)> _failureReasons = [];
    private int _fcmConfigured;
    private long _inboxBacklog;
    private double? _timeToReadP50;

    public NotificationMetrics(bool fcmCredentialConfigured)
    {
        _fcmConfigured = fcmCredentialConfigured ? 1 : 0;
        _meter = new Meter(MeterName);

        // The one counter that is NOT derived at scrape time: the dispatcher increments it, so a
        // scrape-time query per scrape (which still could not express `type`) is unnecessary.
        _deliveries = _meter.CreateCounter<long>(
            "aveline.notification.delivery",
            description: "Notification delivery attempts by channel and terminal status.");

        _meter.CreateObservableGauge(
            "aveline.notification.fcm_credential_configured",
            () => new[] { new Measurement<int>(Volatile.Read(ref _fcmConfigured)) },
            description: "1 when FCM credentials are configured and push is real, 0 when push is a no-op.");

        _meter.CreateObservableGauge(
            "aveline.notification.inbox_backlog",
            () => new[] { new Measurement<long>(Interlocked.Read(ref _inboxBacklog)) },
            description: "Unread, undismissed inbox items. NOT S-36's notification_backlog.");

        _meter.CreateObservableGauge(
            "aveline.notification.delivery_success_rate",
            () => ObserveDouble(_successRate, "channel"),
            unit: "ratio",
            description: "Delivered / terminal attempts, by channel.");

        _meter.CreateObservableGauge(
            "aveline.notification.inbox_read_rate",
            () => ObserveDouble(_readRate, "type"),
            unit: "ratio",
            description: "Read inbox items / all inbox items, by notification type.");

        _meter.CreateObservableGauge(
            "aveline.notification.dismiss_rate",
            () => ObserveDouble(_dismissRate, "type"),
            unit: "ratio",
            description: "Dismissed inbox items / all inbox items, by notification type.");

        _meter.CreateObservableGauge(
            "aveline.notification.volume_by_type",
            () => ObserveLong(_volumeByType, "type"),
            description: "Notification records by type (current window).");

        _meter.CreateObservableGauge(
            "aveline.notification.push_dispatch_failures",
            () => ObserveLong(_pushFailuresByPlatform, "platform"),
            description: "Failed push deliveries by device platform (current window).");

        _meter.CreateObservableGauge(
            "aveline.notification.failure_reasons",
            ObserveFailureReasons,
            description: "Failed deliveries by channel and normalised error class (current window).");

        _meter.CreateObservableGauge(
            "aveline.notification.time_to_read_p50_minutes",
            ObserveTimeToRead,
            description: "Median minutes from record creation to read; absent below the sample floor.");
    }

    /// <summary>Records one delivery attempt. Called by the dispatcher on the send path.</summary>
    public void RecordDelivery(NotificationChannel channel, DeliveryStatus status)
        => _deliveries.Add(
            1,
            new KeyValuePair<string, object?>("channel", channel.ToString()),
            new KeyValuePair<string, object?>("status", status.ToString()));

    /// <summary>Publishes the derived snapshot. A null value removes the series (BR-7.10).</summary>
    public void Publish(NotificationMetricSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Volatile.Write(ref _fcmConfigured, snapshot.FcmCredentialConfigured ? 1 : 0);
        Interlocked.Exchange(ref _inboxBacklog, snapshot.InboxBacklog);

        lock (_gate)
        {
            Replace(_successRate, snapshot.DeliverySuccessRateByChannel);
            Replace(_readRate, snapshot.InboxReadRateByType);
            Replace(_dismissRate, snapshot.DismissRateByType);
            Replace(_volumeByType, snapshot.VolumeByType);
            Replace(_pushFailuresByPlatform, snapshot.PushDispatchFailuresByPlatform);

            _failureReasons.Clear();
            foreach (var (key, count) in snapshot.FailureReasonsByChannelAndClass)
            {
                var separator = key.IndexOf('|', StringComparison.Ordinal);
                _failureReasons.Add((
                    separator < 0 ? key : key[..separator],
                    separator < 0 ? "other" : key[(separator + 1)..],
                    count));
            }

            _timeToReadP50 = snapshot.TimeToReadP50Minutes;
        }
    }

    public void Dispose() => _meter.Dispose();

    private static void Replace<TValue>(Dictionary<string, TValue> target, IReadOnlyDictionary<string, TValue> source)
    {
        target.Clear();
        foreach (var (key, value) in source)
        {
            target[key] = value;
        }
    }

    private IEnumerable<Measurement<double>> ObserveDouble(Dictionary<string, double> values, string labelName)
    {
        lock (_gate)
        {
            return values
                .Select(pair => new Measurement<double>(pair.Value, new KeyValuePair<string, object?>(labelName, pair.Key)))
                .ToArray();
        }
    }

    private IEnumerable<Measurement<long>> ObserveLong(Dictionary<string, long> values, string labelName)
    {
        lock (_gate)
        {
            return values
                .Select(pair => new Measurement<long>(pair.Value, new KeyValuePair<string, object?>(labelName, pair.Key)))
                .ToArray();
        }
    }

    private IEnumerable<Measurement<long>> ObserveFailureReasons()
    {
        lock (_gate)
        {
            return _failureReasons
                .Select(item => new Measurement<long>(
                    item.Count,
                    new KeyValuePair<string, object?>("channel", item.Channel),
                    new KeyValuePair<string, object?>("error_class", item.ErrorClass)))
                .ToArray();
        }
    }

    private IEnumerable<Measurement<double>> ObserveTimeToRead()
    {
        lock (_gate)
        {
            return _timeToReadP50 is { } value ? [new Measurement<double>(value)] : [];
        }
    }
}
