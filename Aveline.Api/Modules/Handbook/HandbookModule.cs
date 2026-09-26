using Aveline.Api.Modules.Handbook.Repositories;
using Aveline.Api.Modules.Handbook.Services;

namespace Aveline.Api.Modules.Handbook;

/// <summary>
/// Dependency-injection registration for the handbook knowledge base (ADR-025).
///
/// <para>
/// The module deliberately does not register an embedding client: it consumes the
/// <see cref="Aveline.Api.Modules.CustomerConcierge.Services.IEmbeddingService"/> that
/// <c>AddCustomerConciergeModule()</c> already provides, so there is one provider configuration
/// rather than two. Both modules are registered in <c>Program.cs</c>.
/// </para>
/// </summary>
public static class HandbookModule
{
    public static IServiceCollection AddHandbookModule(this IServiceCollection services)
    {
        services.AddScoped<IHandbookRepository, HandbookRepository>();
        services.AddScoped<IHandbookService, HandbookService>();

        return services;
    }
}
