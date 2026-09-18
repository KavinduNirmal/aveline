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
        // Vision client (OpenAI-compatible multimodal vision). BaseAddress defaults to OpenAI and can
        // be overridden via the "Vision:BaseUrl" config key.
        services.AddHttpClient<IVisionService, VisionService>((sp, client) =>
        {
            var config = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var baseUrl = config["Vision:BaseUrl"] ?? config["Vision__BaseUrl"] ?? config["VISION_BASE_URL"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/");
            }
            else
            {
                var normalized = baseUrl.TrimEnd('/') + "/";
                client.BaseAddress = new Uri(normalized);
            }
        });

        // Repositories
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<ISourcingRequestRepository, SourcingRequestRepository>();
        services.AddScoped<ICustomerMatchRepository, CustomerMatchRepository>();
        services.AddScoped<IOutfitRepository, OutfitRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();

        // Services
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IQrCodeService, QrCodeService>();
        services.AddScoped<IVisualService, VisualService>();

        return services;
    }
}
