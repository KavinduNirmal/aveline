using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Aveline.Api.Configurations;

/// <summary>
/// Registers OpenTelemetry metrics and traces. Metrics are always exposed through the
/// Prometheus scrape endpoint (<c>/metrics</c>); OTLP export is enabled only when
/// <c>Observability:OtlpEndpoint</c> is configured, matching the Python service.
/// </summary>
public static class ObservabilityConfiguration
{
    /// <summary>Meter and ActivitySource name for Aveline API instrumentation.</summary>
    public const string InstrumentationName = "Aveline.Api";

    public static IServiceCollection AddAvelineObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var otlpEndpoint = configuration["Observability:OtlpEndpoint"];
        var exportOverOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("aveline-api"))
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(InstrumentationName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddPrometheusExporter();

                if (exportOverOtlp)
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otlpEndpoint!));
                }
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(InstrumentationName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation();

                if (exportOverOtlp)
                {
                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otlpEndpoint!));
                }
            });

        return services;
    }
}
