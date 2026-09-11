using Aveline.Api.Modules.ApiAccess.Repositories;
using Aveline.Api.Modules.ApiAccess.Services;

namespace Aveline.Api.Modules.ApiAccess;

/// <summary>Dependency injection and routing registration for the API-access module.</summary>
public static class ApiAccessModule
{
    public static IServiceCollection AddApiAccessModule(this IServiceCollection services)
    {
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        return services;
    }

    public static IEndpointRouteBuilder MapApiAccessEndpoints(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
