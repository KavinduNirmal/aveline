using System.Net.Http;
using Aveline.Api.Modules.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Conversations.Media;

/// <summary>
/// Registers the pasted-image-URL fetcher (unit U4.2). Self-contained so the DI wiring is one
/// call from the owning lane, not an edit this unit has to make to <c>Program.cs</c> or
/// <c>ServiceClientsConfiguration.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// The typed-client shape follows the repository convention
/// (<c>ServiceClientsConfiguration.cs:27-40</c>), with one deliberate difference: this client
/// carries <b>no</b> delegating handler. The agent client's <c>InternalServiceAuthHandler</c>
/// exists because that client authenticates; this one must not, so it adds none. The primary
/// handler is <see cref="ImageUrlPinningHandler"/>, which refuses to connect to anything the
/// fetcher did not validate.
/// </para>
/// <para>
/// <see cref="HttpClient.Timeout"/> is infinite on purpose: the fetcher owns the budget, and it
/// applies it as a cancellation token that also bounds the body read. A second, hidden timeout
/// would make that budget impossible to reason about.
/// </para>
/// </remarks>
public static class ImageUrlFetcherRegistration
{
    /// <summary>Registers <see cref="IImageUrlFetcher"/> and its transport.</summary>
    public static IServiceCollection AddImageUrlFetcher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IImageUrlHostResolver, DnsImageUrlHostResolver>();

        services
            .AddHttpClient<IImageUrlFetcher, ImageUrlFetcher>((_, client) =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.Clear();
            })
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var options = provider.GetRequiredService<IOptions<MediaOptions>>().Value;
                var seconds = Math.Max(1, options.ImageUrlFetchTimeoutSeconds);
                return ImageUrlPinningHandler.Create(TimeSpan.FromSeconds(seconds));
            });

        return services;
    }
}
