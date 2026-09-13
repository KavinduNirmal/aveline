using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aveline.Api.Modules.SystemHealth.HealthChecks;

/// <summary>
/// Downgrades an inner check's <see cref="HealthStatus.Unhealthy"/> to
/// <see cref="HealthStatus.Degraded"/> when the dependency is not critical. A degraded
/// readiness response still returns HTTP 200 (BR-7.2, §8.7).
/// </summary>
public sealed class CriticalityHealthCheck(IHealthCheck inner, bool critical) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.CheckHealthAsync(context, cancellationToken);

        if (result.Status == HealthStatus.Unhealthy && !critical)
        {
            return HealthCheckResult.Degraded(result.Description, result.Exception, result.Data);
        }

        return result;
    }
}
