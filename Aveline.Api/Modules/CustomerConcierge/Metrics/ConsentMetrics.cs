using System.Diagnostics.Metrics;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.CustomerConcierge.Services;

namespace Aveline.Api.Modules.CustomerConcierge.Metrics;

/// <summary>
/// The consent-processing metric family (plan §9.1). One counter, <c>aveline.message.skip</c>,
/// exported as <c>aveline_message_skip_total{reason}</c>: it answers "how many inbound messages
/// were not consent-cleared, and why", which is the number an operator needs to see during a
/// consent-store outage or after a revocation wave.
/// </summary>
/// <remarks>
/// <para>
/// The counter is incremented by the ingress gate in <c>ConversationService</c>, not derived at
/// scrape time. The <c>reason</c> label is a bounded vocabulary
/// (<see cref="ConsentGateReasons"/>); a tenant or customer id would create one series per entity
/// and is forbidden (<c>MetricsCatalog.ForbiddenLabelKeys</c>).
/// </para>
/// <para>
/// <b>What "skip" counts.</b> Any outcome the gate did not clear unconditionally: a revoked
/// customer (<c>consent_revoked</c>), an unavailable consent store (<c>consent_check_unavailable</c>),
/// and a message with no customer context (<c>no_customer_context</c>). The last one is processed
/// (plan §8.4) but was never consent-cleared, so counting it keeps "processed without a consent
/// decision" observable rather than invisible.
/// </para>
/// </remarks>
public sealed class ConsentMetrics : IDisposable
{
    /// <summary>The meter the instrument is created on; registered by ObservabilityConfiguration.</summary>
    public static readonly string MeterName = ObservabilityConfiguration.InstrumentationName;

    /// <summary>The instrument name; the exporter publishes it as <c>aveline_message_skip_total</c>.</summary>
    public const string SkipMetricName = "aveline.message.skip";

    /// <summary>The state gauge name; the exporter publishes it as <c>aveline_consent_state_total</c>.</summary>
    public const string StateMetricName = "aveline.consent.state";

    /// <summary>The bounded label key carrying a <see cref="ConsentGateReasons"/> value.</summary>
    public const string ReasonLabel = "reason";

    /// <summary>The bounded label key carrying a <c>ConsentStatuses</c> value.</summary>
    public const string StatusLabel = "status";

    private readonly Meter _meter;
    private readonly Counter<long> _skips;
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _byStatus = new(StringComparer.Ordinal);

    public ConsentMetrics()
    {
        _meter = new Meter(MeterName);
        _skips = _meter.CreateCounter<long>(
            SkipMetricName,
            description: "Inbound messages not cleared unconditionally by the consent gate, by reason.");
        _meter.CreateObservableGauge(
            StateMetricName,
            ObserveByStatus,
            description: "Customer consent rows by status (current snapshot).");
    }

    /// <summary>Records one message's consent disposition.</summary>
    public void RecordSkip(string reason)
        => _skips.Add(1, new KeyValuePair<string, object?>(ReasonLabel, reason));

    /// <summary>
    /// Publishes the current consent-row counts by status. Follows the notification family's
    /// recorded deviation: an aggregating series that needs a query is exposed as a <b>snapshot
    /// gauge</b> published by a collector pass, never as a counter derived at scrape time, because a
    /// monotonic counter rebuilt from a table needs delta bookkeeping and a counter that
    /// double-counts or resets is worse than an honest gauge (plan N-6, <c>ConsentMetricCollector</c>).
    /// </summary>
    public void PublishByStatus(IReadOnlyDictionary<string, long> countsByStatus)
    {
        ArgumentNullException.ThrowIfNull(countsByStatus);

        lock (_gate)
        {
            _byStatus.Clear();
            foreach (var (status, count) in countsByStatus)
            {
                _byStatus[status] = count;
            }
        }
    }

    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<long>> ObserveByStatus()
    {
        lock (_gate)
        {
            return _byStatus
                .Select(pair => new Measurement<long>(
                    pair.Value, new KeyValuePair<string, object?>(StatusLabel, pair.Key)))
                .ToArray();
        }
    }
}
