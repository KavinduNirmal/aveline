using Aveline.Api.Modules.Integrations.Repositories;
using Aveline.Api.Modules.Integrations.Services;

namespace Aveline.Api.Modules.Integrations;

/// <summary>
/// Dependency-injection registration for the Integrations (tenant credentials) module.
/// The encryption service is a singleton; its key is read lazily from configuration.
/// </summary>
public static class IntegrationsModule
{
    public static IServiceCollection AddIntegrationsModule(this IServiceCollection services)
    {
        services.AddSingleton<ICredentialEncryptionService, CredentialEncryptionService>();
        services.AddScoped<IIntegrationCredentialRepository, IntegrationCredentialRepository>();
        services.AddScoped<IIntegrationService, IntegrationService>();

        return services;
    }
}
