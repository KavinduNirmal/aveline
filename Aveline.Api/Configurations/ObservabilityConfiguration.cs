using System.Diagnostics;
using OpenTelemetry;
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
                // A bearer credential in a trace backend is the same exposure as one in a log file.
                // The ASP.NET Core instrumentation creates the request activity before any
                // middleware runs, so `url.path` already carries the media token by the time the
                // pipeline executes; it can only be removed on the way out, before an exporter
                // reads the activity. Registered first so every export processor sees redacted data.
                tracing.AddProcessor(new CredentialAttributeRedactionProcessor());

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

/// <summary>
/// Removes credential-bearing paths from every span before it is exported, so a trace backend
/// (Jaeger, OTLP) never stores a live bearer token (migration plan §7.7, "token never logged").
/// </summary>
/// <remarks>
/// <para>
/// This runs in <see cref="OnEnd"/>, after the instrumentation has written every attribute and
/// before the export processors read the activity, and it rewrites in place rather than removing:
/// the attribute keeps its shape and only the credential-bearing value changes.
/// </para>
/// <para>
/// It sweeps <em>all</em> string attributes and the display name rather than a fixed list of
/// well-known keys (<c>url.path</c>, <c>url.full</c>, <c>http.target</c>, …), because the semantic
/// conventions move between instrumentation versions and a fixed list would silently stop covering
/// the leak. The rule itself lives in <see cref="RequestPathRedaction"/>, next to the ones the log
/// sinks use.
/// </para>
/// </remarks>
internal sealed class CredentialAttributeRedactionProcessor : BaseProcessor<Activity>
{
    /// <inheritdoc />
    public override void OnEnd(Activity data)
    {
        if (RequestPathRedaction.TryRedactValue(data.DisplayName, out var displayName)
            && !string.Equals(displayName, data.DisplayName, StringComparison.Ordinal))
        {
            data.DisplayName = displayName;
        }

        // Collected first: an activity's tag list cannot be mutated while it is enumerated.
        List<KeyValuePair<string, string>>? rewrites = null;
        foreach (var tag in data.TagObjects)
        {
            if (tag.Value is string value
                && RequestPathRedaction.TryRedactValue(value, out var redacted)
                && !string.Equals(redacted, value, StringComparison.Ordinal))
            {
                (rewrites ??= []).Add(new KeyValuePair<string, string>(tag.Key, redacted));
            }
        }

        if (rewrites is null)
        {
            return;
        }

        foreach (var (key, value) in rewrites)
        {
            data.SetTag(key, value);
        }
    }
}
