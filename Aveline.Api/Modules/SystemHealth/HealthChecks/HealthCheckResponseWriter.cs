using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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

        var payload = new
        {
            status = report.Status.ToString(),
            version = new
            {
                gitSha = deployment.GitSha,
                buildTime = deployment.BuildTime,
                assemblyVersion = deployment.AssemblyVersion,
                environment = deployment.Environment,
            },
            totalDurationMs = (int)Math.Round(report.TotalDuration.TotalMilliseconds),
            checks,
        };

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
