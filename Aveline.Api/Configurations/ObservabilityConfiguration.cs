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

    /// <summary>
    /// The meter <c>EventBusMetrics</c> creates its counters and publish-latency histogram on.
    /// It is a second meter because the instruments were authored before this registration
    /// existed; registering only <see cref="InstrumentationName"/> dropped all four silently.
    /// </summary>
    public const string EventingMeterName = "Aveline.Api.Eventing";

    /// <summary>
    /// The meter Npgsql 10 creates its eleven <c>db.client.*</c> instruments on (pool counts,
    /// pool maximum, pending requests, timeouts, operation duration and failures). They are
    /// produced by default and were discarded before this registration, which is what closed M-9.
    /// </summary>
    public const string NpgsqlMeterName = "Npgsql";

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
                    // Closes M-1: the event-bus instruments previously reached no scrape.
                    .AddMeter(EventingMeterName)
                    // Closes M-9: Npgsql's pool and query instruments were produced and dropped.
                    .AddMeter(NpgsqlMeterName)
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
