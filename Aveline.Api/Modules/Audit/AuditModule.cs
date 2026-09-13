using Aveline.Api.Modules.Audit.Endpoints;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;

namespace Aveline.Api.Modules.Audit;

/// <summary>Dependency-injection registration for the cross-module audit module.</summary>
public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IAuditRedactor, AuditRedactor>();
        services.AddScoped<IAuditService, AuditService>();

        return services;
    }

    /// <summary>Maps the <c>/api/v1</c>-relative audit read routes (call on the v1 group).</summary>
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        AuditEndpoints.MapAuditEndpoints(endpoints);
        return endpoints;
    }
}
