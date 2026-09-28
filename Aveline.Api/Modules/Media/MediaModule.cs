using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
/// <para>
/// Unit U2.1 adds the protected-tier pieces: the signer, the asset locator, the access service,
/// the mint service, and the nonce store. The nonce store is Redis when Redis is configured and a
/// fail-closed stub otherwise, so a host without Redis can mint a <c>vision.analyze</c> token but
/// can never serve one (strategy §3.8).
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

        AddProtectedTier(services, configuration);
        return services;
    }

    /// <summary>
    /// Registers the protected-tier services (unit U2.1). Separate so the provider switch stays
    /// readable; both run from <see cref="AddMediaModule"/>.
    /// </summary>
    private static void AddProtectedTier(IServiceCollection services, IConfiguration configuration)
    {
        // The same registration the conversations module uses, so a host that resolves the clock
        // gets one instance.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IMediaUrlSigner, HmacMediaUrlSigner>();
        services.AddScoped<IMediaAssetLocator, DatabaseMediaAssetLocator>();
        services.AddScoped<MediaAccessService>();
        services.AddScoped<MediaTokenMintService>();

        // Resolved here rather than through CacheConfiguration so the media module does not reach
        // into the configuration namespace; it is the same value the shared multiplexer keys on.
        var redisConnection = configuration["Redis:ConnectionString"]
                              ?? configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSingleton<IMediaTokenNonceStore, RedisMediaTokenNonceStore>();
        }
        else
        {
            services.AddSingleton<IMediaTokenNonceStore, UnavailableMediaTokenNonceStore>();
        }
    }
}
