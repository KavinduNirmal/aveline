using System.Reflection;

namespace Aveline.Api.Modules.SystemHealth.HealthChecks;

/// <summary>Deployment metadata exposed on <c>/health/ready</c> (FR-7.3).</summary>
public sealed record DeploymentInfo(string GitSha, string BuildTime, string AssemblyVersion, string Environment)
{
    public static DeploymentInfo Unknown { get; } = new("unknown", "unknown", "0.0.0", "unknown");
}

/// <summary>
/// Resolves the deployment metadata once at startup. Configuration
/// (<c>Deployment:GitSha</c>, <c>Deployment:BuildTime</c>) wins so a CI build can inject
/// the exact commit; otherwise assembly attributes are used.
/// </summary>
public sealed class DeploymentInfoProvider
{
    private static readonly Assembly ApiAssembly = typeof(DeploymentInfoProvider).Assembly;

    public DeploymentInfoProvider(IConfiguration configuration, IHostEnvironment environment)
    {
        var informational = ApiAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        var gitSha = configuration["Deployment:GitSha"];
        if (string.IsNullOrWhiteSpace(gitSha) && !string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            gitSha = plus >= 0 ? informational[(plus + 1)..] : informational;
        }

        var buildTime = configuration["Deployment:BuildTime"];
        if (string.IsNullOrWhiteSpace(buildTime))
        {
            try
            {
                buildTime = File.GetLastWriteTimeUtc(ApiAssembly.Location)
                    .ToString("yyyy-MM-ddTHH:mm:ssZ");
            }
            catch (IOException)
            {
                buildTime = "unknown";
            }
        }

        Current = new DeploymentInfo(
            gitSha ?? "unknown",
            buildTime ?? "unknown",
            ApiAssembly.GetName().Version?.ToString() ?? "0.0.0",
            environment.EnvironmentName);
    }

    public DeploymentInfo Current { get; }
}
