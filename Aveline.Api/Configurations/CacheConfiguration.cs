using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Aveline.Api.Configurations;

public static class CacheConfiguration
{
    /// <summary>
    /// Resolves the Redis connection string from <c>Redis:ConnectionString</c> or
    /// <c>ConnectionStrings:Redis</c>, or <c>null</c> when Redis is not configured.
    /// </summary>
    public static string? ResolveRedisConnectionString(IConfiguration configuration)
        => configuration["Redis:ConnectionString"]
           ?? configuration.GetConnectionString("Redis");

    public static IServiceCollection AddAvelineCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConn = ResolveRedisConnectionString(configuration);

        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            // Register a single shared IConnectionMultiplexer so the distributed cache and
            // the event bus (ADR-014) reuse one Redis connection.
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(redisConn));

            services.AddSingleton<IDistributedCache>(sp =>
            {
                var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
                return new RedisCache(Options.Create(new RedisCacheOptions
                {
                    InstanceName = "aveline:",
                    ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer),
                }));
            });
        }
        else
        {
            // Fallback to in-memory distributed cache if Redis connection is not specified
            services.AddDistributedMemoryCache();
        }

        return services;
    }
}
