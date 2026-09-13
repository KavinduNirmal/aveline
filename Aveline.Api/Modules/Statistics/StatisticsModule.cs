using Aveline.Api.Configurations;
using Aveline.Api.Modules.Statistics.Endpoints;
using Aveline.Api.Modules.Statistics.Jobs;
using Aveline.Api.Modules.Statistics.Repositories;
using Aveline.Api.Modules.Statistics.Services;
using Aveline.Api.Modules.Statistics.Telemetry;
using StackExchange.Redis;

namespace Aveline.Api.Modules.Statistics;

/// <summary>
/// Dependency injection and routing registration for the statistics module, following the
/// module-registration convention established by <c>BillingModule</c>.
/// </summary>
public static class StatisticsModule
{
    public static IServiceCollection AddStatisticsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TelemetryOptions>(
            configuration.GetSection(TelemetryOptions.SectionName));
        services.Configure<QuotaOptions>(
            configuration.GetSection(QuotaOptions.SectionName));

        services.AddScoped<IAgentRunRepository, AgentRunRepository>();
        services.AddScoped<IAgentStatisticsService, AgentStatisticsService>();
        services.AddScoped<IAgentRunIngestService, AgentRunIngestService>();

        services.AddScoped<IApiMetricRepository, ApiMetricRepository>();
        services.AddScoped<IApiRequestLogRepository, ApiRequestLogRepository>();
        services.AddScoped<IApiStatisticsService, ApiStatisticsService>();
        services.AddScoped<IQuotaService, QuotaService>();

        services.AddScoped<ISystemMetricRepository, SystemMetricRepository>();

        // Atomic Redis counters when Redis is configured; the process-local fallback mirrors
        // InMemoryDistributedJobLock and is only for local development and tests.
        if (!string.IsNullOrWhiteSpace(CacheConfiguration.ResolveRedisConnectionString(configuration)))
        {
            services.AddSingleton<IQuotaCounterStore>(sp =>
                new RedisQuotaCounterStore(sp.GetRequiredService<IConnectionMultiplexer>()));
        }
        else
        {
            services.AddSingleton<IQuotaCounterStore, InMemoryQuotaCounterStore>();
        }

        services.AddSingleton<TelemetryChannel>();
        services.AddSingleton<ApiKeyUsageAggregator>();
        services.AddSingleton<IApiKeyUsageSink>(sp => sp.GetRequiredService<ApiKeyUsageAggregator>());

        services.AddHostedService<AgentStatsRetentionJob>();
        services.AddHostedService<StaleAgentRunJob>();
        services.AddHostedService<ApiTelemetryWriter>();
        services.AddHostedService<ApiQuotaResetJob>();
        services.AddHostedService<ApiStatsRollupJob>();
        services.AddHostedService<ApiStatsRetentionJob>();
        services.AddHostedService<ApiRequestLogPartitionJob>();
        services.AddHostedService(sp => sp.GetRequiredService<ApiKeyUsageAggregator>());
        services.AddHostedService<SystemMetricCollector>();
        services.AddHostedService<SystemMetricRetentionJob>();

        return services;
    }

    /// <summary>Maps the <c>/api/v1</c>-relative statistics routes (call on the v1 group).</summary>
    public static IEndpointRouteBuilder MapStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAgentStatisticsEndpoints();
        endpoints.MapApiStatisticsEndpoints();
        return endpoints;
    }

    /// <summary>Maps the root-relative internal ingest routes (call on the application).</summary>
    public static IEndpointRouteBuilder MapStatisticsInternalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapInternalAgentRunEndpoints();
        return endpoints;
    }
}
