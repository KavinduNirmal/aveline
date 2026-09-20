using Aveline.Api.Configurations;
using Aveline.Api.Modules.Revenue.Endpoints;
using Aveline.Api.Modules.Revenue.Services;

namespace Aveline.Api.Modules.Revenue;

/// <summary>
/// Dependency injection and routing registration for the revenue module.
/// </summary>
public static class RevenueModule
{
    public static IServiceCollection AddRevenueModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RevenueOptions>(configuration.GetSection(RevenueOptions.SectionName));

        services.AddScoped<IIncomeLedgerService, IncomeLedgerService>();
        services.AddScoped<IRevenueStatisticsService, RevenueStatisticsService>();
        services.AddScoped<RevenueCache>();

        return services;
    }

    /// <summary>
    /// Maps the revenue routes under the caller's group. Named differently from the endpoint class's
    /// own entry points on purpose: two same-named extension methods would resolve to this one and
    /// recurse rather than delegate.
    /// </summary>
    public static IEndpointRouteBuilder MapRevenueModuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapRevenueWriteEndpoints();
        app.MapRevenueReadEndpoints();
        return app;
    }
}
