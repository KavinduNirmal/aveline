using System.Diagnostics.Metrics;
using Aveline.Api.Configurations;

namespace Aveline.Api.Modules.Privacy.Metrics;

/// <summary>The bounded vocabulary of privacy delivery outcomes (plan §9.1).</summary>
public static class PrivacyDeliverySignals
{
    /// <summary>An OTP or acknowledgement was handed to the provider.</summary>
    public const string Delivered = "delivered";

    /// <summary>A privacy message was not delivered (provider refused, channel absent, store down).</summary>
    public const string Failed = "failed";
}

/// <summary>
/// The privacy delivery metric family (plan §9.1): how many OTPs and acknowledgements actually left
/// the building, and how many did not. It exists now because the OTP sender and the acknowledgement
/// both need to report their outcome, and a "we could not send" signal is the one an operator must
/// see during a provider outage.
/// </summary>
/// <remarks>
/// No tenant label: that would create one series per organization
/// (<c>MetricsCatalog.ForbiddenLabelKeys</c>). Full catalogue wiring is Phase 6.
/// </remarks>
public sealed class PrivacyDeliveryMetrics : IDisposable
{
    /// <summary>The meter the instruments are created on; shared with the rest of the API's metrics.</summary>
    public static readonly string MeterName = ObservabilityConfiguration.InstrumentationName;

    public const string DeliveredMetricName = "aveline.privacy.delivery.delivered";
    public const string FailedMetricName = "aveline.privacy.delivery.failed";

    private readonly Meter _meter;
    private readonly Counter<long> _delivered;
    private readonly Counter<long> _failed;

    /// <summary>The most recent signal recorded. Exposed for tests, like <c>DisclosureMetrics</c>.</summary>
    public string? LastSignal { get; private set; }

    public PrivacyDeliveryMetrics()
    {
        _meter = new Meter(MeterName);
        _delivered = _meter.CreateCounter<long>(
            DeliveredMetricName, description: "Privacy messages handed to the provider, by kind.");
        _failed = _meter.CreateCounter<long>(
            FailedMetricName, description: "Privacy messages that were not delivered, by kind and reason.");
    }

    /// <summary>Records a delivered privacy message.</summary>
    public void RecordDelivered(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        LastSignal = $"delivered:{kind}";
        _delivered.Add(1, new KeyValuePair<string, object?>("kind", kind));
    }

    /// <summary>Records a privacy message that did not reach the provider.</summary>
    public void RecordFailed(string kind, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        LastSignal = $"failed:{kind}:{reason}";
        _failed.Add(
            1,
            new KeyValuePair<string, object?>("kind", kind),
            new KeyValuePair<string, object?>("reason", reason));
    }

    public void Dispose() => _meter.Dispose();
}

/// <summary>
/// The bounded <c>kind</c> values for <see cref="PrivacyDeliveryMetrics"/>. They are also the
/// <c>kind</c> label of a <c>NotificationType.PrivacyDeliveryFailed</c> notification (privacy plan
/// §11 Phase 6): one vocabulary for the metric and the notification, so a dashboard series and an
/// inbox alert can be correlated by name.
/// </summary>
public static class PrivacyDeliveryKinds
{
    /// <summary>An opt-out OTP.</summary>
    public const string Otp = "otp";

    /// <summary>The single opt-out acknowledgement.</summary>
    public const string OptOutAcknowledgement = "opt_out_ack";

    /// <summary>The first-contact transparency disclosure (Phase 6's notification-only kind).</summary>
    public const string Disclosure = "disclosure";
}
