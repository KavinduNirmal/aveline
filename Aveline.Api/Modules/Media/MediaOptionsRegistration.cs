using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// Registers the media configuration surface (strategy §3.4): the options and the one place the
/// Cloudinary credential is resolved, explicitly, at startup.
/// </summary>
/// <remarks>
/// S0 stops here on purpose. The provider selection (<c>IMediaStorage</c> →
/// <c>DatabaseMediaStorage</c>/<c>CloudinaryMediaStorage</c>) is unit U0.7's
/// <c>MediaModule</c>, which extends this extension point; keeping storage out of S0 lets the app
/// start and the whole existing suite run without any provider implementation.
/// </remarks>
public static class MediaOptionsRegistration
{
    /// <summary>Binds <see cref="MediaOptions"/> and <see cref="CloudinaryOptions"/>.</summary>
    public static IServiceCollection AddMediaOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MediaOptions>(configuration.GetSection(MediaOptions.SectionName));

        services.Configure<CloudinaryOptions>(options =>
        {
            configuration.GetSection(CloudinaryOptions.SectionName).Bind(options);

            // The credential is resolved once, here, from configuration. The SDK's ambient
            // `new Cloudinary()` environment read is never used (strategy §3.4; C11).
            if (CloudinaryUrlParser.TryResolve(configuration, out var credentials, out _))
            {
                options.CloudName = credentials.CloudName;
                options.ApiKey = credentials.ApiKey;
                options.ApiSecret = credentials.ApiSecret;
            }
        });

        return services;
    }
}
