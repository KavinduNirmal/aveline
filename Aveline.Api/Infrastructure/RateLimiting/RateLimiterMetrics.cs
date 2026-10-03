using System.Diagnostics.Metrics;

namespace Aveline.Api.Infrastructure.RateLimiting;

/// <summary>
/// The rate limiter's metric family. One instrument matters here:
/// <c>aveline.rate_limiter.fail_open</c>, which counts requests that were allowed <em>because the
/// counter store was unreachable</em> rather than because they were within budget.
/// </summary>
/// <remarks>
/// <see cref="DistributedRateLimiter"/> fails open by design (see its remarks and
/// <c>docs/ADR/ADR-030-rate-limiter-fail-open.md</c>). That makes a cache outage invisible in the
/// HTTP metrics — traffic simply proceeds — so without this counter an operator cannot tell that
/// abuse protection is off. The <c>scope</c> label is a bounded set of endpoint budgets, not a
/// per-user value.
/// </remarks>
public static class RateLimiterMetrics
{
    /// <summary>The meter the instrument is created on; shared with the rest of the API's metrics.</summary>
    public static readonly string MeterName = Aveline.Api.Configurations.ObservabilityConfiguration.InstrumentationName;

    public const string FailOpenMetricName = "aveline.rate_limiter.fail_open";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> FailOpenCounter = Meter.CreateCounter<long>(
        FailOpenMetricName,
        unit: "{request}",
        description: "Requests allowed because the rate-limit counter store was unavailable.");

    /// <summary>Records one fail-open decision for <paramref name="scopeKey"/>.</summary>
    public static void RecordFailOpen(string scopeKey) =>
        FailOpenCounter.Add(
            1,
            new KeyValuePair<string, object?>("scope", Sanitise(scopeKey)));

    /// <summary>
    /// Collapses a scope key to a bounded label. Scope keys are built from configuration
    /// (<c>invitations:create:&lt;ip&gt;</c>, <c>privacy:verify:&lt;phone&gt;</c>), so the trailing
    /// identifier is dropped rather than minting one metric series per client.
    /// </summary>
    internal static string Sanitise(string scopeKey)
    {
        var separator = scopeKey.LastIndexOf(':');
        return separator > 0 ? scopeKey[..separator] : scopeKey;
    }
}
