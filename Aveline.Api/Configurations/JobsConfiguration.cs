using Aveline.Api.Common.Jobs;
using StackExchange.Redis;

namespace Aveline.Api.Configurations;

/// <summary>
/// Registers the distributed job lock used by scheduled background jobs. The Redis
/// implementation is used when Redis is configured; otherwise a process-local lock keeps
/// local development and the test suite working.
/// </summary>
public static class JobsConfiguration
{
    public const int DefaultLockSeconds = 300;

    public static IServiceCollection AddAvelineJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var configuredSeconds = configuration.GetValue("Jobs:DistributedLockSeconds", DefaultLockSeconds);
        var lease = TimeSpan.FromSeconds(configuredSeconds > 0 ? configuredSeconds : DefaultLockSeconds);

        var redisConnection = CacheConfiguration.ResolveRedisConnectionString(configuration);
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSingleton<IDistributedJobLock>(provider =>
                new RedisDistributedJobLock(
                    provider.GetRequiredService<IConnectionMultiplexer>(),
                    lease));
        }
        else
        {
            services.AddSingleton<IDistributedJobLock>(new InMemoryDistributedJobLock(lease));
        }

        return services;
    }
}
