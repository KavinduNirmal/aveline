using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The one place <see cref="IMediaStorage"/> is registered (strategy §3.1, §5.1 S0). The provider
/// is chosen once, here, from <c>Media:Provider</c>; no caller branches on it.
/// </summary>
/// <remarks>
/// The Cloudinary implementation arrives with unit U1.1, so this unit registers only what exists:
/// the <c>database</c> branch. The <c>cloudinary</c> branch is registered as a factory that
/// refuses to resolve, rather than silently falling back to the database adapter — the seam may
/// never quietly store bytes somewhere other than where the configuration says. A half-configured
/// <c>cloudinary</c> provider is still refused earlier and more specifically by
/// <see cref="MediaOptionsValidator"/> at startup; this unit does not weaken that.
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
                // CloudinaryMediaStorage lands with U1.1 (strategy §5.1 S1). Until then the seam
                // must not resolve to the database adapter under a cloudinary configuration.
                services.AddScoped<IMediaStorage>(_ => throw new InvalidOperationException(
                    "Media:Provider=cloudinary is selected, but CloudinaryMediaStorage does not "
                    + "exist yet (unit U1.1). Set Media:Provider=database until then."));
                break;

            default:
                throw new InvalidOperationException(
                    $"Media:Provider value '{provider}' is not recognised. The valid values are "
                    + "'database' and 'cloudinary'.");
        }

        return services;
    }
}
