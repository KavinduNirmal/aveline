using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;

namespace Aveline.Api.Modules.Billing;

/// <summary>
/// Dependency injection and routing registration for the Billing module.
/// </summary>
public static class BillingModule
{
    public static IServiceCollection AddBillingModule(this IServiceCollection services)
    {
        services.AddScoped<IUsageRepository, UsageRepository>();
        services.AddScoped<IUsageTrackerService, UsageTrackerService>();

        return services;
    }

    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapUsageEndpoints();
        return app;
    }
}
