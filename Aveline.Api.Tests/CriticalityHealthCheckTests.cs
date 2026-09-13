using Aveline.Api.Modules.SystemHealth.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #180 — a non-critical dependency failure must degrade readiness, not fail it
/// (BR-7.2, §8.7).
/// </summary>
public class CriticalityHealthCheckTests
{
    [Fact]
    public async Task NonCritical_Unhealthy_DowngradesToDegraded()
    {
        var sut = new CriticalityHealthCheck(new StubCheck(HealthStatus.Unhealthy, "down"), critical: false);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task Critical_Unhealthy_StaysUnhealthy()
    {
        var sut = new CriticalityHealthCheck(new StubCheck(HealthStatus.Unhealthy, "down"), critical: true);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Healthy_StaysHealthy_RegardlessOfCriticality()
    {
        var sut = new CriticalityHealthCheck(new StubCheck(HealthStatus.Healthy, null), critical: false);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private sealed class StubCheck(HealthStatus status, string? description) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new HealthCheckResult(status, description));
    }
}
