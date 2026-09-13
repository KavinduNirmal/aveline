using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Aveline.Api.Modules.SystemHealth.HealthChecks;

/// <summary>
/// Writes the documented readiness payload (FR-7.2, FR-7.3). Only a check's description
/// is exposed; exception details are deliberately dropped so a connection string or stack
/// trace can never reach the response (BR-7.3).
/// </summary>
public static class HealthCheckResponseWriter
{
    private const int MaxMessageLength = 300;

    // Known checks are ordered as documented so the contract stays stable.
    private static readonly string[] KnownOrder = ["database", "redis", "agent-service", "clerk-jwks"];

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        var deployment = context.RequestServices?
            .GetService<DeploymentInfoProvider>()?.Current ?? DeploymentInfo.Unknown;

        var checks = report.Entries
            .OrderBy(entry => Array.IndexOf(KnownOrder, entry.Key) is var index && index >= 0 ? index : int.MaxValue)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = (int)Math.Round(entry.Value.Duration.TotalMilliseconds),
                message = Truncate(entry.Value.Description),
            })
            .ToArray();

        // The deployment identity (git SHA, build time, environment) is anonymous debug
        // detail. It is withheld in Production so the public probe cannot fingerprint the
        // release (M-3); non-production environments keep it for operators.
        var includeVersion = !string.Equals(
            deployment.Environment, Environments.Production, StringComparison.OrdinalIgnoreCase);

        var payload = new Dictionary<string, object?>
        {
            ["status"] = report.Status.ToString(),
        };

        if (includeVersion)
        {
            payload["version"] = new
            {
                gitSha = deployment.GitSha,
                buildTime = deployment.BuildTime,
                assemblyVersion = deployment.AssemblyVersion,
                environment = deployment.Environment,
            };
        }

        payload["totalDurationMs"] = (int)Math.Round(report.TotalDuration.TotalMilliseconds);
        payload["checks"] = checks;

        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    private static string? Truncate(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        return message.Length <= MaxMessageLength ? message : message[..MaxMessageLength];
    }
}
