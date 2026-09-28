using System.Diagnostics.Metrics;
using Aveline.Api.Configurations;

namespace Aveline.Api.Modules.Privacy.Metrics;

/// <summary>
/// The bounded <c>status</c> vocabulary for the rights counters (privacy plan §9.1, Phase 6 item
/// 6.3). Constants because the values are metric labels: a counter whose label values drift is
/// useless to alert on.
/// </summary>
/// <remarks>
/// Only outcomes the code can actually distinguish are named. A "verified then completed" export is
/// <see cref="Completed"/>; a refusal before the code was checked is
/// <see cref="VerificationFailed"/>. There is deliberately no per-reason split of the failure: the
/// OTP failure reasons already have their own counter (<c>aveline_otp_failed_total</c>), and a
/// second derivation of the same figure is exactly what the phase's 6.3 note forbids.
/// </remarks>
public static class DataSubjectOutcomes
{
    /// <summary>The export was returned or the erasure committed.</summary>
    public const string Completed = "completed";

    /// <summary>The caller proved the number but no record matched it.</summary>
    public const string NotFound = "not_found";

    /// <summary>The code was not proven (wrong, expired, replayed or over the attempt cap).</summary>
    public const string VerificationFailed = "verification_failed";

    /// <summary>Another erasure holds the lease for the same (organisation, number).</summary>
    public const string Conflict = "conflict";

    /// <summary>The counter store refused before the request was attempted.</summary>
    public const string Unavailable = "unavailable";
}

/// <summary>
/// The data-subject-rights metric family (privacy plan §9.1, Phase 6 item 6.3):
/// <c>aveline_data_export_requests_total{status}</c>,
/// <c>aveline_data_delete_requests_total{status}</c> and
/// <c>aveline_data_delete_time_to_complete_seconds</c>. Together they answer the two questions a
/// rights audit asks - "how many requests came in and how did they end" and "how long did an
/// erasure take" - without a second derivation of any figure that already exists.
/// </summary>
/// <remarks>
/// No tenant or customer label: either would create one series per entity and is forbidden
/// (<c>MetricsCatalog.ForbiddenLabelKeys</c>). The <c>status</c> label is a bounded constant set
/// (<see cref="DataSubjectOutcomes"/>).
/// </remarks>
public sealed class RightsMetrics : IDisposable
{
    /// <summary>The meter the instruments are created on; shared with the rest of the API's metrics.</summary>
    public static readonly string MeterName = ObservabilityConfiguration.InstrumentationName;

    public const string ExportRequestsMetricName = "aveline.data.export_requests";
    public const string DeleteRequestsMetricName = "aveline.data.delete_requests";
    public const string TimeToCompleteMetricName = "aveline.data.delete_time_to_complete";

    /// <summary>The bounded label key carrying a <see cref="DataSubjectOutcomes"/> value.</summary>
    public const string StatusLabel = "status";

    private readonly Meter _meter;
    private readonly Counter<long> _exportRequests;
    private readonly Counter<long> _deleteRequests;
    private readonly Histogram<double> _timeToComplete;

    /// <summary>
    /// The most recent <c>(counter, status)</c> recorded, or the completion duration. Exposed so a
    /// test can assert the metric the caller actually emitted rather than re-deriving it.
    /// </summary>
    public (string Counter, string Status)? LastStatus { get; private set; }

    /// <summary>The most recent erasure completion duration, in seconds.</summary>
    public double? LastTimeToCompleteSeconds { get; private set; }

    public RightsMetrics()
    {
        _meter = new Meter(MeterName);
        _exportRequests = _meter.CreateCounter<long>(
            ExportRequestsMetricName,
            description: "Data-export requests by terminal status.");
        _deleteRequests = _meter.CreateCounter<long>(
            DeleteRequestsMetricName,
            description: "Data-deletion requests by terminal status.");
        _timeToComplete = _meter.CreateHistogram<double>(
            TimeToCompleteMetricName,
            unit: "s",
            description: "Wall-clock seconds from a deletion request to its completion.");
    }

    /// <summary>Records one export request's terminal status.</summary>
    public void RecordExportRequest(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        LastStatus = ("export", status);
        _exportRequests.Add(1, new KeyValuePair<string, object?>(StatusLabel, status));
    }

    /// <summary>Records one deletion request's terminal status.</summary>
    public void RecordDeleteRequest(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        LastStatus = ("delete", status);
        _deleteRequests.Add(1, new KeyValuePair<string, object?>(StatusLabel, status));
    }

    /// <summary>
    /// Records how long a completed erasure took. Recorded once per request, on the first execution:
    /// a replayed request returns a stored result and did not complete again.
    /// </summary>
    public void RecordTimeToComplete(TimeSpan duration)
    {
        var seconds = duration.TotalSeconds < 0 ? 0 : duration.TotalSeconds;
        LastTimeToCompleteSeconds = seconds;
        _timeToComplete.Record(seconds);
    }

    public void Dispose() => _meter.Dispose();
}
