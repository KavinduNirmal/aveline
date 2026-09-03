using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Configurations;

public static class CacheConfiguration
{
    public static IServiceCollection AddAvelineCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConn = configuration["Redis:ConnectionString"]
            ?? configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConn;
                options.InstanceName = "aveline:";
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
