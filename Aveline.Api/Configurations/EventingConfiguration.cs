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
        // Always register the health-check service so /health is available even without Redis.
        services.AddHealthChecks();

        var redisConn = CacheConfiguration.ResolveRedisConnectionString(configuration);
        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            services.AddSingleton<RedisEventBus>();
            services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<RedisEventBus>());
            services.AddHostedService<RedisSubscriptionService>();
            services.AddHostedService<EventingMetricsExporter>();
            services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis");
        }
        else
        {
            services.AddSingleton<IEventBus, InMemoryEventBus>();
        }

        return services;
    }
}
