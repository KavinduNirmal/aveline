using Aveline.Api.Infrastructure.Eventing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Aveline.Api.Configurations;

/// <summary>
/// Registers the Redis event bus (ADR-014). When a Redis connection string is configured
/// the bus is backed by Redis Pub/Sub over the shared <see cref="IConnectionMultiplexer"/>
/// registered by <see cref="CacheConfiguration"/>; otherwise an in-process
/// <see cref="InMemoryEventBus"/> fallback is used so the app (and test suite) runs without
/// Redis.
/// </summary>
public static class EventingConfiguration
{
    public static IServiceCollection AddAvelineEventing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IEventSerializer, SystemTextJsonEventSerializer>();
        services.AddSingleton<EventBusMetrics>();
        // Health-check infrastructure is registered here so /health exists even without Redis.
        // The redis check itself is registered by SystemHealthModule, which owns criticality.
        services.AddHealthChecks();

        var redisConn = CacheConfiguration.ResolveRedisConnectionString(configuration);
        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            services.AddSingleton<RedisEventBus>();
            services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<RedisEventBus>());
            services.AddHostedService<RedisSubscriptionService>();
            services.AddHostedService<EventingMetricsExporter>();
        }
        else
        {
            services.AddSingleton<IEventBus, InMemoryEventBus>();
        }

        return services;
    }
}
