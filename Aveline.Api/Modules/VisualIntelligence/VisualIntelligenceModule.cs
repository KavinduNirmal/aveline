using Aveline.Api.Modules.VisualIntelligence.Repositories;
using Aveline.Api.Modules.VisualIntelligence.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Modules.VisualIntelligence;

/// <summary>
/// Dependency-injection registration for the Visual Intelligence &amp; Sourcing module (Slice 2).
/// </summary>
public static class VisualIntelligenceModule
{
    public static IServiceCollection AddVisualIntelligenceModule(this IServiceCollection services)
    {
        // Repositories
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<ISourcingRequestRepository, SourcingRequestRepository>();
        services.AddScoped<ICustomerMatchRepository, CustomerMatchRepository>();
        services.AddScoped<IOutfitRepository, OutfitRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();

        // Services
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IVisualService, VisualService>();

        return services;
    }
}
