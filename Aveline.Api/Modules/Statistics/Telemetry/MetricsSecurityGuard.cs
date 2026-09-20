namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// Startup guard for the Prometheus scrape credential (plan S-1/R-1). <c>/metrics</c> is exposed
/// by the API and holds every process, HTTP and business metric; on a default deployment the
/// working credential was the committed internal service token, which is a known value in the
/// repository. Production must therefore configure a dedicated token rather than inherit it.
/// </summary>
public static class MetricsSecurityGuard
{
    /// <summary>
    /// Fails fast when Production runs with no <c>Metrics:ScrapeToken</c>, mirroring
    /// <see cref="TelemetrySecurityGuard.EnsureIpHashSaltForProduction"/>. Other environments stay
    /// permissive so local development and tests need no secret.
    /// </summary>
    public static void EnsureScrapeTokenForProduction(
        IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        var isProduction = string.Equals(
            environment.EnvironmentName, Environments.Production, StringComparison.OrdinalIgnoreCase);

        if (!isProduction)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(configuration["Metrics:ScrapeToken"]))
        {
            throw new InvalidOperationException(
                "Metrics:ScrapeToken must be configured in Production. Set the "
                + "METRICS_SCRAPE_TOKEN environment variable (Metrics__ScrapeToken) to at least "
                + "32 random bytes so /metrics is not readable with the committed internal "
                + "service token.");
        }
    }
}
