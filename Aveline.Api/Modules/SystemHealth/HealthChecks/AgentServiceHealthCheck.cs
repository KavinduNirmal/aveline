using Aveline.Api.Infrastructure.Integrations;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aveline.Api.Modules.SystemHealth.HealthChecks;

/// <summary>
/// Readiness check for the Python agent service. Returns Unhealthy on failure; the
/// criticality wrapper decides whether that fails readiness (§8.7).
/// </summary>
public sealed class AgentServiceHealthCheck(IAgentServiceClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));

            using var response = await client.GetAsync("/health/ready", timeout.Token);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Agent service reachable.")
                : HealthCheckResult.Unhealthy("Agent service is unreachable.");
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Agent service is unreachable.");
        }
    }
}
