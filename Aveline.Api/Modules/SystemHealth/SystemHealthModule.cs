using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.SystemHealth.Endpoints;
using Aveline.Api.Modules.SystemHealth.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aveline.Api.Modules.SystemHealth;

/// <summary>
/// Registers the readiness checks and their endpoints. Availability of each dependency
/// decides whether its failure is critical (fail readiness) or merely degraded (§8.7).
/// </summary>
public static class SystemHealthModule
{
    public static IServiceCollection AddSystemHealthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton<DeploymentInfoProvider>();
        services.AddTransient<DatabaseHealthCheck>();
        services.AddTransient<AgentServiceHealthCheck>();
        services.AddTransient<ClerkJwksHealthCheck>();

        var redisIsCritical = configuration.GetValue("Observability:RedisIsCritical", false);
        var agentIsCritical = configuration.GetValue("Observability:AgentIsCritical", true);

        var healthChecks = services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database");

        // Redis is only a dependency when it is configured; a failure degrades readiness
        // unless Observability:RedisIsCritical is set.
        if (!string.IsNullOrWhiteSpace(CacheConfiguration.ResolveRedisConnectionString(configuration)))
        {
            services.AddTransient<RedisHealthCheck>();
            healthChecks.Add(new HealthCheckRegistration(
                "redis",
                provider => new CriticalityHealthCheck(
                    provider.GetRequiredService<RedisHealthCheck>(), redisIsCritical),
                failureStatus: null,
                tags: null));
        }

        healthChecks.Add(new HealthCheckRegistration(
            "agent-service",
            provider => new CriticalityHealthCheck(
                provider.GetRequiredService<AgentServiceHealthCheck>(), agentIsCritical),
            failureStatus: null,
            tags: null));

        healthChecks.AddCheck<ClerkJwksHealthCheck>("clerk-jwks");

        return services;
    }
}
