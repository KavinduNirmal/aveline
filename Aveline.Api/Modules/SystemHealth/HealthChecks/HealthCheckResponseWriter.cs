using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Aveline.Api.Modules.SystemHealth.HealthChecks;

/// <summary>
/// Writes the documented readiness payload (FR-7.2, FR-7.3). Only a check's description
/// is exposed; exception details are deliberately dropped so a connection string or stack
/// trace can never reach the response (BR-7.3).
///
/// <para>
/// The per-check breakdown names every internal dependency (PostgreSQL, Redis, the agent service,
/// the Clerk JWKS endpoint) and reports its latency. That is operational detail an unauthenticated
/// probe should not receive, so it is disclosed only to an authenticated internal caller. Anonymous
/// callers - the orchestrator's liveness/readiness probe, which needs nothing more than the
/// aggregate verdict - receive <c>status</c> and <c>totalDurationMs</c> only. The route stays
/// anonymous for exactly that reason (security assessment F-1.5).
/// </para>
/// </summary>
public static class HealthCheckResponseWriter
{
    private const int MaxMessageLength = 300;

    /// <summary>The role that unlocks the detailed per-check breakdown.</summary>
    private const string InternalRole = Aveline.Api.Infrastructure.Integrations.InternalTokenAuthenticationHandler.RoleName;

    // Known checks are ordered as documented so the contract stays stable.
    private static readonly string[] KnownOrder = ["database", "redis", "agent-service", "clerk-jwks"];

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        var deployment = context.RequestServices?
            .GetService<DeploymentInfoProvider>()?.Current ?? DeploymentInfo.Unknown;

        // The deployment identity (git SHA, build time, environment) and the per-dependency
        // breakdown are operator detail. Both are withheld from anonymous callers; the version
        // block additionally stays withheld in Production (M-3).
        var includeDetail = IsInternalCaller(context.User);

        var payload = new Dictionary<string, object?>
        {
            ["status"] = report.Status.ToString(),
        };

        var includeVersion = !string.Equals(
            deployment.Environment, Environments.Production, StringComparison.OrdinalIgnoreCase);

        if (includeVersion && includeDetail)
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

        if (includeDetail)
        {
            payload["checks"] = report.Entries
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
        }

        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    /// <summary>
    /// True when the caller presented a valid internal service token. Anonymous probes and
    /// end-user sessions both receive the reduced payload.
    /// </summary>
    private static bool IsInternalCaller(ClaimsPrincipal? principal)
        => principal?.IsInRole(InternalRole) == true;

    private static string? Truncate(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        return message.Length <= MaxMessageLength ? message : message[..MaxMessageLength];
    }
}
