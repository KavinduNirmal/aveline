using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The one place <see cref="IMediaStorage"/> is registered (strategy §3.1, §5.1 S0). The provider
/// is chosen once, here, from <c>Media:Provider</c>; no caller branches on it.
/// </summary>
/// <remarks>
/// <para>
/// <c>database</c> resolves the byte-store pass-through; <c>cloudinary</c> resolves
/// <see cref="CloudinaryMediaStorage"/> over the SDK gateway (unit U1.1). A half-configured
/// <c>cloudinary</c> provider is refused earlier and more specifically by
/// <see cref="MediaOptionsValidator"/> at startup; the seam may never quietly store bytes
/// somewhere other than where the configuration says.
/// </para>
/// <para>
/// The Cloudinary branch is registered only when it is selected, so a <c>database</c> host gains
/// no Cloudinary client and no HTTP client.
/// </para>
/// </remarks>
public static class MediaModule
{
    /// <summary>Registers the media provider seam selected by <c>Media:Provider</c>.</summary>
    public static IServiceCollection AddMediaModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Absent means the documented safe default, `database` (strategy §3.4).
        var provider = configuration.GetValue("Media:Provider", MediaProvider.Database);

        switch (provider)
        {
            case MediaProvider.Database:
                services.AddScoped<IMediaStorage, DatabaseMediaStorage>();
                break;

            case MediaProvider.Cloudinary:
                services.AddHttpClient<ICloudinaryGateway, CloudinaryApiGateway>();
                services.AddScoped<CloudinaryRetryPolicy>(sp => new CloudinaryRetryPolicy(
                    sp.GetRequiredService<IOptions<CloudinaryOptions>>().Value,
                    sp.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Aveline.Api.Modules.Media.CloudinaryRetryPolicy")));
                services.AddScoped<IMediaStorage, CloudinaryMediaStorage>();
                break;

            default:
                throw new InvalidOperationException(
                    $"Media:Provider value '{provider}' is not recognised. The valid values are "
                    + "'database' and 'cloudinary'.");
        }

        return services;
    }
}
