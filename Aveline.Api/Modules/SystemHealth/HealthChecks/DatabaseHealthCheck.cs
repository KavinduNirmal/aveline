using Aveline.Api.Infrastructure.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aveline.Api.Modules.SystemHealth.HealthChecks;

/// <summary>
/// Readiness check for PostgreSQL. Uses the provider-agnostic
/// <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.CanConnectAsync"/>
/// so the in-memory provider used by tests also answers.
/// </summary>
public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database reachable.")
                : HealthCheckResult.Unhealthy("Database is unreachable.");
        }
        catch (Exception)
        {
            // BR-7.3: never surface the connection string or the exception message.
            return HealthCheckResult.Unhealthy("Database is unreachable.");
        }
    }
}
