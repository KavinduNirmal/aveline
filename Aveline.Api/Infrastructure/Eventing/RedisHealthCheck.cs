using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Aveline.Api.Infrastructure.Eventing;

/// <summary>
/// Health check that verifies Redis connectivity by issuing a <c>PING</c>. Registered only
/// when a Redis connection string is configured (see <c>EventingConfiguration</c>).
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _connection;

    public RedisHealthCheck(IConnectionMultiplexer connection) => _connection = connection;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _connection.GetDatabase();
            var pong = await db.PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy($"Redis responded in {pong.TotalMilliseconds:F0} ms.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis is unreachable.", ex);
        }
    }
}
