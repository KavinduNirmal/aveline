namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// Startup guard for telemetry secrets (M-1). The IP hash salt is the only thing that
/// stops the stored SHA-256 of a client address being brute-forced; an empty salt in
/// Production is a configuration error, not a warning.
/// </summary>
public static class TelemetrySecurityGuard
{
    /// <summary>
    /// Fails fast when Production runs with no <c>Telemetry:IpHashSalt</c>. Other
    /// environments stay permissive so local development and tests need no secret.
    /// </summary>
    public static void EnsureIpHashSaltForProduction(
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

        if (string.IsNullOrWhiteSpace(configuration["Telemetry:IpHashSalt"]))
        {
            throw new InvalidOperationException(
                "Telemetry:IpHashSalt must be configured in Production. Set the "
                + "Telemetry__IpHashSalt environment variable to a high-entropy secret so "
                + "the hashed client addresses stored by request telemetry cannot be "
                + "brute-forced.");
        }
    }
}
