using Aveline.Api.Modules.Revenue.Endpoints;
using Aveline.Api.Modules.Revenue.Services;

namespace Aveline.Api.Modules.Revenue;

/// <summary>
/// Dependency injection and routing registration for the revenue module.
/// </summary>
public static class RevenueModule
{
    public static IServiceCollection AddRevenueModule(this IServiceCollection services)
    {
        services.AddScoped<IIncomeLedgerService, IncomeLedgerService>();
        return services;
    }

    /// <summary>
    /// Maps the revenue routes under the caller's group. Named differently from the endpoint class's
    /// own entry point on purpose: two same-named extension methods would resolve to this one and
    /// recurse rather than delegate.
    /// </summary>
    public static IEndpointRouteBuilder MapRevenueModuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapRevenueWriteEndpoints();
        return app;
    }
}
