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

        return services;
    }

    public static IEndpointRouteBuilder MapStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints;
    }
}
