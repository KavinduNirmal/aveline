using Aveline.Api.Modules.Integrations.Metrics;
using Aveline.Api.Modules.Integrations.Repositories;
using Aveline.Api.Modules.Integrations.Services;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddHostedService<IntegrationHealthService>();

        // The outbound family (privacy plan §6.2/§6.4). The metric family is a singleton like the
        // notification and consent families; the channel and the dispatcher are SCOPED because they
        // hold the request's AppDbContext.
        //
        // Consequence for Phase 3/4: a background sender (the disclosure dispatcher, the OTP job)
        // must create its own DI scope and resolve IOutboundMessagingService from it. Resolving this
        // interface from the root/singleton scope would either throw at a scope-validation build or
        // capture a disposed DbContext. That is deliberately not papered over here with a singleton
        // registration, because a singleton channel holding a scoped AppDbContext is a real bug, not
        // a convenience.
        services.AddSingleton<OutboundMetrics>();
        services.AddScoped<IOutboundChannel, WhatsAppOutboundChannel>();
        services.AddScoped<IOutboundMessagingService, OutboundMessagingService>();
        // The same scoped instance serves both interfaces, so a caller that needs the registry and
        // a caller that needs the dispatcher share one channel set.
        services.AddScoped<IOutboundChannelRegistry>(sp => (OutboundMessagingService)sp.GetRequiredService<IOutboundMessagingService>());

        return services;
    }
}
