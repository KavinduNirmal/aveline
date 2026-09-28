using System.Diagnostics.Metrics;
using Aveline.Api.Configurations;

namespace Aveline.Api.Modules.Privacy.Metrics;

/// <summary>
/// The bounded vocabulary of disclosure outcomes (plan §9.1). Constants because the values are
/// metric labels and alert keys: free text would make a counter useless to alert on.
/// </summary>
public static class DisclosureSignals
{
    /// <summary>A first-contact disclosure was sent.</summary>
    public const string Shown = "shown";

    /// <summary>A first contact ended without a disclosure (provider refused, channel absent, or no signing key).</summary>
    public const string Unshown = "unshown";
}

/// <summary>
/// The disclosure metric family (plan §9.1): <c>aveline_disclosure_shown_total</c> and
/// <c>aveline_disclosure_unshown_total</c>. Together they answer the operator's question that
/// matters for transparency - "are customers being disclosed to, and if not, how many are not".
/// </summary>
/// <remarks>
/// The counters carry no labels on purpose. A tenant or customer id would create one series per
/// entity (<c>MetricsCatalog.ForbiddenLabelKeys</c>), and the only split that matters operationally
/// is shown/unshown, which is already two series. Full dashboard/catalogue wiring is Phase 6; the
/// instruments exist now because they are two lines and the disclosure path is where they are
/// produced.
/// </remarks>
public sealed class DisclosureMetrics : IDisposable
{
    /// <summary>The meter the instruments are created on; shared with the rest of the API's metrics.</summary>
    public static readonly string MeterName = ObservabilityConfiguration.InstrumentationName;

    /// <summary>The shown counter; exported as <c>aveline_disclosure_shown_total</c>.</summary>
    public const string ShownMetricName = "aveline.disclosure.shown";

    /// <summary>The unshown counter; exported as <c>aveline_disclosure_unshown_total</c>.</summary>
    public const string UnshownMetricName = "aveline.disclosure.unshown";

    private readonly Meter _meter;
    private readonly Counter<long> _shown;
    private readonly Counter<long> _unshown;

    /// <summary>
    /// The most recent signal recorded. Exposed so a test can assert the metric the service actually
    /// emitted rather than re-deriving it from the returned outcome.
    /// </summary>
    public string? LastSignal { get; private set; }

    public DisclosureMetrics()
    {
        _meter = new Meter(MeterName);
        _shown = _meter.CreateCounter<long>(
            ShownMetricName,
            description: "First-contact disclosures sent, by disclosure version.");
        _unshown = _meter.CreateCounter<long>(
            UnshownMetricName,
            description: "First contacts that ended without a disclosure.");
    }

    /// <summary>Records that a disclosure was sent.</summary>
    public void RecordShown()
    {
        LastSignal = DisclosureSignals.Shown;
        _shown.Add(1);
    }

    /// <summary>Records that a first contact ended without a disclosure.</summary>
    public void RecordUnshown()
    {
        LastSignal = DisclosureSignals.Unshown;
        _unshown.Add(1);
    }

    public void Dispose() => _meter.Dispose();
}
