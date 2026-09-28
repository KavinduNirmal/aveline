using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Aveline.Api.Configurations;

namespace Aveline.Api.Modules.Payments.Services;

/// <summary>
/// The payment metric family's instruments (plan §12.1). Created on the <c>Aveline.Api</c> meter,
/// which <c>ObservabilityConfiguration</c> registers via <c>AddMeter</c>, so every series reaches
/// the Prometheus scrape without a second registration site.
/// </summary>
/// <remarks>
/// <para>
/// Names are the dotted instrument names of §12.1; the exporter appends its suffix from the unit
/// (a counter with no unit gains <c>_total</c>), which is why the unit is written next to each
/// instrument rather than left to a dashboard to remember.
/// </para>
/// <para>
/// This type owns only instrument creation and the two gauge snapshots it can derive from
/// configuration and from the adapter itself. The orchestration services (P2-B2) call the
/// <c>Record*</c> members; that keeps a single definition of every series in one file.
/// </para>
/// </remarks>
public sealed class PaymentMetrics : IDisposable
{
    /// <summary>The meter every payment instrument is created on.</summary>
    public static readonly string MeterName = ObservabilityConfiguration.InstrumentationName;

    private readonly Meter _meter;
    private readonly Counter<long> _intentCreated;
    private readonly Counter<long> _settlement;
    private readonly Histogram<double> _settlementLatency;
    private readonly Counter<long> _webhookReceived;
    private readonly Counter<long> _webhookVerificationFailures;
    private readonly Counter<long> _refund;
    private readonly Counter<long> _providerErrors;

    private readonly ConcurrentDictionary<string, int> _mockProviderActive = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _unprocessedBacklog = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _unreconciledIntents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _verificationFailures = new(StringComparer.Ordinal);

    public PaymentMetrics()
    {
        _meter = new Meter(MeterName);

        _intentCreated = _meter.CreateCounter<long>(
            "aveline.payment.intent.created",
            unit: "intents",
            description: "Payment intents created, by provider and purpose.");

        _settlement = _meter.CreateCounter<long>(
            "aveline.payment.settlement",
            unit: "settlements",
            description: "Settlement attempts by provider, purpose and outcome. outcome=mismatch is Critical.");

        _settlementLatency = _meter.CreateHistogram<double>(
            "aveline.payment.settlement_latency_ms",
            unit: "ms",
            description: "Provider-call to settled latency, by provider and purpose.");

        _webhookReceived = _meter.CreateCounter<long>(
            "aveline.payment.webhook.received",
            unit: "events",
            description: "Payment webhooks received, by provider and event type.");

        _webhookVerificationFailures = _meter.CreateCounter<long>(
            "aveline.payment.webhook.verification_failures",
            unit: "events",
            description: "Payment webhooks rejected before parsing, by provider and reason.");

        _refund = _meter.CreateCounter<long>(
            "aveline.payment.refund",
            unit: "refunds",
            description: "Refund attempts by provider and outcome. outcome=provider_error is alertable.");

        _providerErrors = _meter.CreateCounter<long>(
            "aveline.payment.provider.errors",
            unit: "calls",
            description: "Provider-call failures by provider, operation and error code.");

        _meter.CreateObservableGauge(
            "aveline.payment.webhook.unprocessed_backlog",
            () => Observe(_unprocessedBacklog, "provider"),
            unit: "events",
            description: "Inbound provider events still holding a null ProcessedAt, by provider.");

        _meter.CreateObservableGauge(
            "aveline.payment.mock_provider_active",
            () => Observe(_mockProviderActive, "provider"),
            description: "1 when the mock payment provider is active for a provider key, 0 otherwise. Alert on == 1 outside Development.");

        _meter.CreateObservableGauge(
            "aveline.payment.unreconciled_intents",
            () => Observe(_unreconciledIntents, "provider"),
            unit: "intents",
            description: "Intents whose provider state has not been confirmed within the reconciliation window, by provider.");
    }

    // ------------------------------------------------------------ counters

    /// <summary>Records one intent creation.</summary>
    public void RecordIntentCreated(string provider, string purpose)
        => _intentCreated.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("purpose", purpose));

    /// <summary>Records one settlement attempt and its outcome (<c>succeeded</c>/<c>failed</c>/<c>mismatch</c>).</summary>
    public void RecordSettlement(string provider, string purpose, string outcome)
        => _settlement.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("purpose", purpose),
            new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>Records the wall-clock latency of one settlement, in milliseconds.</summary>
    public void RecordSettlementLatency(string provider, string purpose, double milliseconds)
        => _settlementLatency.Record(
            milliseconds,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("purpose", purpose));

    /// <summary>Records one inbound webhook after it was accepted for processing.</summary>
    public void RecordWebhookReceived(string provider, string type)
        => _webhookReceived.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("type", type));

    /// <summary>
    /// Records one rejected webhook. <paramref name="reason"/> is <c>signature</c>, <c>timestamp</c>,
    /// <c>missing</c>, or <c>payload</c> for a body that could not be parsed.
    /// </summary>
    public void RecordWebhookVerificationFailure(string provider, string reason)
    {
        _webhookVerificationFailures.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("reason", reason));

        _verificationFailures.AddOrUpdate($"{provider}|{reason}", 1, (_, current) => current + 1);
    }

    /// <summary>Records one refund attempt and its outcome.</summary>
    public void RecordRefund(string provider, string outcome)
        => _refund.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>Records one provider-call failure.</summary>
    public void RecordProviderError(string provider, string operation, string errorCode)
        => _providerErrors.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("error_code", errorCode));

    // ------------------------------------------------------------ gauges

    /// <summary>Publishes the unprocessed-webhook backlog for a provider.</summary>
    public void SetWebhookUnprocessedBacklog(string provider, long count)
        => _unprocessedBacklog[provider] = count;

    /// <summary>
    /// Publishes whether the mock adapter is active for a provider key (guardrail 4 of plan §7.4).
    /// The series always exists as 0 or 1 so a rule can test <c>== 1</c> rather than a disappearance.
    /// </summary>
    public void SetMockProviderActive(string provider, bool active)
        => _mockProviderActive[provider] = active ? 1 : 0;

    /// <summary>Publishes the number of intents awaiting provider reconciliation, by provider.</summary>
    public void SetUnreconciledIntents(string provider, long count)
        => _unreconciledIntents[provider] = count;

    // ------------------------------------------------------------ test/diagnostic reads

    /// <summary>The gauge value for a provider key; false when the series is absent or zero.</summary>
    public bool IsMockProviderActive(string provider)
        => _mockProviderActive.TryGetValue(provider, out var value) && value == 1;

    /// <summary>How many webhooks were rejected for a (provider, reason) pair. For tests and diagnostics.</summary>
    public long VerificationFailureCount(string provider, string reason)
        => _verificationFailures.TryGetValue($"{provider}|{reason}", out var value) ? value : 0;

    public void Dispose() => _meter.Dispose();

    private static IEnumerable<Measurement<long>> Observe(
        ConcurrentDictionary<string, long> values, string labelName)
        => values
            .Select(pair => new Measurement<long>(
                pair.Value, new KeyValuePair<string, object?>(labelName, pair.Key)))
            .ToArray();

    private static IEnumerable<Measurement<int>> Observe(
        ConcurrentDictionary<string, int> values, string labelName)
        => values
            .Select(pair => new Measurement<int>(
                pair.Value, new KeyValuePair<string, object?>(labelName, pair.Key)))
            .ToArray();
}
