using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Aveline.Api.Modules.Conversations.Media;

/// <summary>
/// Builds the single HTTP handler the pasted-image-URL fetcher uses, configured so that its two
/// security-critical behaviours are properties of the shipped transport rather than conventions
/// a caller has to remember (salon §7.5 items 6, 7 and 12).
/// </summary>
/// <remarks>
/// <para>
/// <b>Pinning.</b> The handler's <see cref="SocketsHttpHandler.ConnectCallback"/> connects the TCP
/// socket to the address carried on the request under <see cref="PinnedAddressKey"/>, never to
/// the address the platform resolves from the URI. The request URI is left untouched, so TLS SNI
/// and the <c>Host</c> header still name the original host. There is no second DNS lookup between
/// validation and connect, which is the classic rebinding window this closes.
/// </para>
/// <para>
/// <b>Fail closed.</b> A request that carries no pinned address is refused before a socket is
/// opened: the handler cannot reach a host the fetcher did not validate, even if a future caller
/// forgets to pin.
/// </para>
/// <para>
/// <b>No redirects, no cookies, no credentials.</b> <see cref="SocketsHttpHandler.AllowAutoRedirect"/>
/// and <see cref="SocketsHttpHandler.UseCookies"/> are off; the redirect policy lives in the
/// fetcher so every hop is re-validated, and no ambient cookie jar or auth delegating handler is
/// attached to this client (<c>InternalServiceAuthHandler</c> belongs to the agent client, which
/// authenticates; this one must not).
/// </para>
/// </remarks>
internal static class ImageUrlPinningHandler
{
    /// <summary>
    /// The per-request option the fetcher sets to the validated address and the connect callback
    /// reads back. Reading it from <c>InitialRequestMessage</c> keeps pinning per-request on a
    /// pooled handler, so the fetcher needs no throwaway handler per hop.
    /// </summary>
    public static readonly HttpRequestOptionsKey<IPAddress> PinnedAddressKey =
        new("Aveline.ImageUrl.PinnedAddress");

    /// <summary>
    /// The handler. <paramref name="connectTimeout"/> bounds the TCP handshake independently of
    /// the fetcher's total budget, so a peer that accepts nothing cannot hold a pool slot.
    /// </summary>
    public static SocketsHttpHandler Create(TimeSpan connectTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(connectTimeout, TimeSpan.Zero);

        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = connectTimeout,
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = ConnectPinnedAsync,
        };
    }

    private static async ValueTask<Stream> ConnectPinnedAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var request = context.InitialRequestMessage;

        if (request is null
            || !request.Options.TryGetValue(PinnedAddressKey, out var pinned)
            || pinned is null)
        {
            throw new InvalidOperationException(
                "The image URL fetcher refused to connect: the request carries no validated address to pin.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket
                .ConnectAsync(new IPEndPoint(pinned, context.DnsEndPoint.Port), cancellationToken)
                .ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
