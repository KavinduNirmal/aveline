namespace Aveline.Api.Tests;

/// <summary>
/// The scrape-based metrics tests build their own meter provider, but the OpenTelemetry SDK
/// resolves instruments through a process-global meter registry: a concurrently running test that
/// has created an <c>AvelineMetrics</c> instance (whose meter name is fixed, because it must match
/// the application's) contributes its measurements to every provider that subscribes to that name.
/// Serialising these classes is what keeps an "absent series" assertion truthful.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class MetricsExpositionCollection
{
    public const string Name = "metrics-exposition";
}
