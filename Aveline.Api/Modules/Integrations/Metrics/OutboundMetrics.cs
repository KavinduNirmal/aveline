using System.Diagnostics.Metrics;
using Aveline.Api.Configurations;

namespace Aveline.Api.Modules.Integrations.Metrics;

/// <summary>
/// The outbound-delivery metric family (plan §9.1). One counter,
/// <c>aveline.outbound_message.result</c>, exported as
/// <c>aveline_outbound_message_result_total{channel,result}</c>: it answers "how much outbound
/// traffic did we attempt, on which channel, and how did it end", which is the number an operator
/// needs to distinguish a boutique with no credentials (<c>skipped</c>) from a Meta outage
/// (<c>failed</c>).
/// </summary>
/// <remarks>
/// Both labels are bounded vocabularies (<c>whatsapp</c>/<c>instagram</c> and the
/// <see cref="OutboundOutcomes"/> values). A tenant, customer or message id would create one
/// series per entity and is forbidden (<c>MetricsCatalog.ForbiddenLabelKeys</c>).
/// </remarks>
public sealed class OutboundMetrics : IDisposable
{
    /// <summary>The meter the instrument is created on; registered by ObservabilityConfiguration.</summary>
    public static readonly string MeterName = ObservabilityConfiguration.InstrumentationName;

    /// <summary>The instrument name; the exporter publishes it as <c>aveline_outbound_message_result_total</c>.</summary>
    public const string ResultMetricName = "aveline.outbound_message.result";

    /// <summary>The bounded label key carrying an <see cref="IOutboundChannel.ChannelKey"/>.</summary>
    public const string ChannelLabel = "channel";

    /// <summary>The bounded label key carrying an <see cref="OutboundOutcomes"/> value.</summary>
    public const string ResultLabel = "result";

    private readonly Meter _meter;
    private readonly Counter<long> _results;

    /// <summary>
    /// The most recent <c>(channel, result)</c> recorded. Exposed so a test can assert the metric
    /// the service actually emitted instead of re-deriving it from the returned record.
    /// </summary>
    public (string Channel, string Result)? LastResult { get; private set; }

    public OutboundMetrics()
    {
        _meter = new Meter(MeterName);
        _results = _meter.CreateCounter<long>(
            ResultMetricName,
            description: "Outbound message attempts by channel and result.");
    }

    /// <summary>Records one outbound attempt's outcome.</summary>
    public void RecordResult(string channel, string result)
    {
        LastResult = (channel, result);
        _results.Add(
            1,
            new KeyValuePair<string, object?>(ChannelLabel, channel),
            new KeyValuePair<string, object?>(ResultLabel, result));
    }

    public void Dispose() => _meter.Dispose();
}
