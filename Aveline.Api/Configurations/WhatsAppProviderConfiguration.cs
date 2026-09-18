using Aveline.Api.Modules.Integrations.Services.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Configurations;

/// <summary>
/// Registers the WhatsApp (Meta) outbound provider as a typed <see cref="HttpClient"/>.
/// Base URL and API version come from <c>WhatsApp:BaseUrl</c> and <c>WhatsApp:ApiVersion</c>.
/// </summary>
public static class WhatsAppProviderConfiguration
{
    public const string DefaultBaseUrl = "https://graph.facebook.com";
    public const string DefaultApiVersion = "v21.0";

    public static IServiceCollection AddWhatsAppProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var baseUrl = configuration["WhatsApp:BaseUrl"] ?? DefaultBaseUrl;
        var apiVersion = configuration["WhatsApp:ApiVersion"] ?? DefaultApiVersion;

        services.AddHttpClient<IWhatsAppService, WhatsAppService>(client =>
        {
            client.BaseAddress = new Uri($"{baseUrl.TrimEnd('/')}/{apiVersion.TrimStart('/')}/");
        });

        return services;
    }
}
