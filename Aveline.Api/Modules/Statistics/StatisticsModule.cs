using Aveline.Api.Modules.Statistics.Endpoints;
using Aveline.Api.Modules.Statistics.Jobs;
using Aveline.Api.Modules.Statistics.Repositories;
using Aveline.Api.Modules.Statistics.Services;

namespace Aveline.Api.Modules.Statistics;

/// <summary>
/// Dependency injection and routing registration for the statistics module, following the
/// module-registration convention established by <c>BillingModule</c>.
/// </summary>
public static class StatisticsModule
{
    public static IServiceCollection AddStatisticsModule(this IServiceCollection services)
    {
        services.AddScoped<IAgentRunRepository, AgentRunRepository>();
        services.AddScoped<IAgentStatisticsService, AgentStatisticsService>();
        services.AddScoped<IAgentRunIngestService, AgentRunIngestService>();
        services.AddHostedService<AgentStatsRetentionJob>();
        services.AddHostedService<StaleAgentRunJob>();

        return services;
    }

    /// <summary>Maps the <c>/api/v1</c>-relative statistics routes (call on the v1 group).</summary>
    public static IEndpointRouteBuilder MapStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAgentStatisticsEndpoints();
        return endpoints;
    }

    /// <summary>Maps the root-relative internal ingest routes (call on the application).</summary>
    public static IEndpointRouteBuilder MapStatisticsInternalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapInternalAgentRunEndpoints();
        return endpoints;
    }
}
