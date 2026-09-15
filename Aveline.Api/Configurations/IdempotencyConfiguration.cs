using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;

namespace Aveline.Api.Configurations;

/// <summary>Registers the idempotency replay store and its endpoint filter.</summary>
public static class IdempotencyConfiguration
{
    public static IServiceCollection AddAvelineIdempotency(this IServiceCollection services)
    {
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();
        services.AddScoped<Modules.Billing.Endpoints.IdempotencyEndpointFilter>();
        return services;
    }
}
