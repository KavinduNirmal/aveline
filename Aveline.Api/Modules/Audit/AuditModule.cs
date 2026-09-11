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
}
