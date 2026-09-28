using System.Diagnostics.Metrics;
using Aveline.Api.Configurations;

namespace Aveline.Api.Modules.Privacy.Metrics;

/// <summary>
/// The bounded vocabulary of privacy-endpoint refusals (privacy plan §9.1,
/// <c>privacy_endpoint_rate_limited_total</c>). Constants because the values are metric labels:
/// free text would make the counter useless to alert on, and the difference between "the customer
/// asked too often" and "the store is down" is exactly what an operator needs at 3am.
/// </summary>
public static class PrivacyRateLimitReasons
{
    /// <summary>The per-address start budget is spent.</summary>
    public const string IpBudget = "ip_budget";

    /// <summary>The per-number send budget is spent.</summary>
    public const string PhoneBudget = "phone_budget";

    /// <summary>The per-address verification budget is spent.</summary>
    public const string VerifyIpBudget = "verify_ip_budget";

    /// <summary>The per-address data-rights budget is spent.</summary>
    public const string RightsIpBudget = "rights_ip_budget";

    /// <summary>The counter store could not be read; the request was refused (fail closed).</summary>
    public const string StoreUnavailable = "store_unavailable";
}

/// <summary>
/// The OTP metric family (plan §9.1, Phase 6 item 6.3): <c>aveline_otp_issued_total</c>,
/// <c>aveline_otp_verified_total</c>, <c>aveline_otp_failed_total</c> and
/// <c>aveline_privacy_endpoint_rate_limited_total</c>. The refusal counter is the one that has to
/// work during a Redis outage, which is why the service records it from the fail-closed path.
/// </summary>
/// <remarks>
/// No tenant or customer labels: those create one series per entity
/// (<c>MetricsCatalog.ForbiddenLabelKeys</c>). The failure counter's <c>reason</c> label is a bounded
/// constant set. The counters are incremented on the path that made the decision - the OTP service
/// for the start budgets, the endpoint for the verification and data-rights budgets - so the series
/// means "refusals", not "a value derived at scrape time".
/// </remarks>
public sealed class OtpMetrics : IDisposable
{
    /// <summary>The meter the instruments are created on; shared with the rest of the API's metrics.</summary>
    public static readonly string MeterName = ObservabilityConfiguration.InstrumentationName;

    public const string IssuedMetricName = "aveline.otp.issued";
    public const string VerifiedMetricName = "aveline.otp.verified";
    public const string FailedMetricName = "aveline.otp.failed";
    public const string EndpointRateLimitedMetricName = "aveline.privacy.endpoint_rate_limited";

    private readonly Meter _meter;
    private readonly Counter<long> _issued;
    private readonly Counter<long> _verified;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _rateLimited;

    /// <summary>
    /// The most recent signal recorded, in the form <c>"issued"</c>, <c>"verified"</c>,
    /// <c>"failed:{reason}"</c> or <c>"rate_limited:{reason}"</c>. Exposed so a test asserts what the
    /// service emitted rather than re-deriving it from the returned outcome.
    /// </summary>
    public string? LastSignal { get; private set; }

    public OtpMetrics()
    {
        _meter = new Meter(MeterName);
        _issued = _meter.CreateCounter<long>(IssuedMetricName, description: "OTP codes issued.");
        _verified = _meter.CreateCounter<long>(VerifiedMetricName, description: "OTP codes verified.");
        _failed = _meter.CreateCounter<long>(
            FailedMetricName, description: "OTP verifications that did not succeed, by reason.");
        _rateLimited = _meter.CreateCounter<long>(
            EndpointRateLimitedMetricName,
            description: "Privacy endpoint requests refused by a rate limit before any work, by reason.");
    }

    /// <summary>Records a code issued.</summary>
    public void RecordIssued()
    {
        LastSignal = "issued";
        _issued.Add(1);
    }

    /// <summary>Records a code verified. The caller records this only after the revocation succeeds.</summary>
    public void RecordVerified()
    {
        LastSignal = "verified";
        _verified.Add(1);
    }

    /// <summary>Records a failed verification, with its bounded reason label.</summary>
    public void RecordFailed(string reason)
    {
        LastSignal = $"failed:{reason}";
        _failed.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }

    /// <summary>
    /// Records a privacy endpoint refused by a budget. Called for the start, verification and
    /// data-rights budgets, so "the customer was throttled" is one series rather than three.
    /// </summary>
    public void RecordRateLimited(string reason)
    {
        LastSignal = $"rate_limited:{reason}";
        _rateLimited.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }

    public void Dispose() => _meter.Dispose();
}
