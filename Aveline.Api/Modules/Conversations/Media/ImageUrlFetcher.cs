using System.Net;
using System.Security.Cryptography;
using System.Text;
using Aveline.Api.Common.Media;
using Aveline.Api.Modules.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Conversations.Media;

/// <summary>
/// The SSRF-guarded fetcher specified in the salon plan §7.5. Every one of its fourteen
/// behaviours is a test in <c>Aveline.Api.Tests/ImageUrlFetcherTests.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// The order of the guard is the guard. A hop is parsed strictly, filtered by scheme, refused if
/// it carries user information or a non-default port, filtered by the optional host allow-list,
/// resolved by us, refused unless <em>every</em> resolved address is public, and only then
/// pinned. Redirects are followed by hand, so that entire sequence runs again on each hop; the
/// HTTP stack never resolves or connects on its own and never follows a redirect.
/// </para>
/// <para>
/// The size cap is a property of how the body is read, not a check applied afterwards: the
/// reader asks each hop for at most <c>MaxFileBytes + 1</c> bytes and stops the moment the cap is
/// crossed. <c>ReadAsByteArrayAsync</c> would make the cap a lie.
/// </para>
/// </remarks>
public sealed class ImageUrlFetcher : IImageUrlFetcher
{
    /// <summary>The streaming read size. The reader never requests more than the cap allows.</summary>
    private const int ReadChunkBytes = 64 * 1024;

    private const int HttpsDefaultPort = 443;
    private const int HttpDefaultPort = 80;

    private readonly HttpClient _http;
    private readonly IOptions<MediaOptions> _options;
    private readonly IImageUrlHostResolver _resolver;
    private readonly ILogger<ImageUrlFetcher> _logger;

    public ImageUrlFetcher(
        HttpClient http,
        IOptions<MediaOptions> options,
        IImageUrlHostResolver resolver,
        ILogger<ImageUrlFetcher> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
        _options = options;
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FetchedImage> FetchAsync(string? imageUrl, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var uri = ParseAbsolute(imageUrl);

        try
        {
            // The kill switch is first: disabled means no resolution, no socket, no log of the
            // URL — a refusal the caller maps to an explicit 400 (strategy §4 C7).
            if (!options.ImageUrlUploadEnabled)
            {
                throw Refuse(
                    ImageUrlFetchReasons.Disabled,
                    "Pasted image URLs are not accepted by this deployment; upload the image file instead.");
            }

            if (uri is null)
            {
                throw Refuse(
                    ImageUrlFetchReasons.Unparseable,
                    "The image URL is not an absolute URL; upload the image file instead.");
            }

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.ImageUrlFetchTimeoutSeconds)));

            var current = uri;
            for (var redirects = 0; ;)
            {
                // Steps 1-6 run for the initial URL and again for every redirect target.
                var pinned = await ValidateAsync(current, options, budget.Token).ConfigureAwait(false);

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Options.Set(ImageUrlPinningHandler.PinnedAddressKey, pinned);

                using var response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token)
                    .ConfigureAwait(false);

                if (IsRedirect(response.StatusCode))
                {
                    if (redirects >= Math.Max(0, options.ImageUrlMaxRedirects))
                    {
                        throw Refuse(
                            ImageUrlFetchReasons.TooManyRedirects,
                            "The image URL redirected more times than the configured limit.");
                    }

                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        throw Refuse(
                            ImageUrlFetchReasons.InvalidRedirect,
                            "The image URL redirected without a location.");
                    }

                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    redirects++;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw Refuse(
                        ImageUrlFetchReasons.FetchFailed,
                        "The image URL could not be retrieved.");
                }

                var fetched = await ReadImageAsync(response, budget.Token).ConfigureAwait(false);

                _logger.LogInformation(
                    "Pasted image URL fetched. host={Host} urlHash={UrlHash} bytes={Bytes} contentType={ContentType}",
                    HostFor(uri),
                    UrlHash(imageUrl),
                    fetched.Bytes.Length,
                    fetched.ContentType);

                return fetched;
            }
        }
        catch (ImageUrlFetchException refusal)
        {
            LogRefusal(refusal.Reason);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller's own cancellation is not a refusal and must propagate untouched.
            throw;
        }
        catch (OperationCanceledException)
        {
            throw RefuseAndLog(
                ImageUrlFetchReasons.Timeout,
                "The image URL did not respond within the fetch budget.");
        }
        catch (Exception exception)
        {
            // Fail closed: every other failure becomes the typed refusal the caller expects, and
            // only the exception's type is logged. Its message may quote the URL, so it is never
            // recorded (salon §7.5 item 13).
            throw RefuseAndLog(
                ImageUrlFetchReasons.FetchFailed,
                "The image URL could not be retrieved.",
                exception.GetType().Name);
        }

        void LogRefusal(string reason) => _logger.LogWarning(
            "Pasted image URL refused. reason={Reason} host={Host} urlHash={UrlHash}",
            reason,
            HostFor(uri),
            UrlHash(imageUrl));

        ImageUrlFetchException RefuseAndLog(string reason, string message, string? error = null)
        {
            if (error is null)
            {
                LogRefusal(reason);
            }
            else
            {
                _logger.LogWarning(
                    "Pasted image URL refused. reason={Reason} host={Host} urlHash={UrlHash} error={Error}",
                    reason,
                    HostFor(uri),
                    UrlHash(imageUrl),
                    error);
            }

            return Refuse(reason, message);
        }
    }

    /// <summary>
    /// Steps 1-6 for one hop: scheme, credentials, port, the optional host allow-list, then our
    /// own resolution with an all-addresses-public rule. Returns the address to pin.
    /// </summary>
    private async Task<IPAddress> ValidateAsync(
        Uri uri, MediaOptions options, CancellationToken cancellationToken)
    {
        EnsureScheme(uri, options);
        EnsureNoCredentials(uri);
        EnsureDefaultPort(uri);
        EnsureHostAllowed(uri, options);

        var host = uri.DnsSafeHost;
        var addresses = await _resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses is null || addresses.Count == 0)
        {
            throw Refuse(
                ImageUrlFetchReasons.UnresolvableHost,
                "The image URL host did not resolve to any address.");
        }

        foreach (var address in addresses)
        {
            if (!ImageUrlAddressPolicy.IsPublic(address))
            {
                throw Refuse(
                    ImageUrlFetchReasons.NonPublicAddress,
                    "The image URL resolves to an address that is not public.");
            }
        }

        // Every address passed, so any of them is safe to pin. The first is the resolver's
        // preferred answer, and pinning is what makes the choice race-free.
        return addresses[0];
    }

    private static void EnsureScheme(Uri uri, MediaOptions options)
    {
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            if (options.AllowInsecureImageFetch)
            {
                return;
            }

            throw Refuse(
                ImageUrlFetchReasons.InsecureSchemeNotAllowed,
                "The image URL is not HTTPS, and insecure image fetching is disabled.");
        }

        // ftp, file, gopher, data and everything else are refused explicitly; the Cloudinary
        // `file` parameter's own ftp arm (C13) is the reason this is a list, not a default.
        throw Refuse(
            ImageUrlFetchReasons.SchemeNotAllowed,
            "The image URL scheme is not allowed; only HTTPS is accepted.");
    }

    private static void EnsureNoCredentials(Uri uri)
    {
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw Refuse(
                ImageUrlFetchReasons.CredentialsNotAllowed,
                "The image URL carries credentials, which are not accepted.");
        }
    }

    private static void EnsureDefaultPort(Uri uri)
    {
        var expected = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            ? HttpDefaultPort
            : HttpsDefaultPort;

        // Uris.Port is the scheme default when the URL names none, so this admits both
        // `https://host/` and `https://host:443/` and refuses every other port.
        if (uri.Port != expected)
        {
            throw Refuse(
                ImageUrlFetchReasons.PortNotAllowed,
                "The image URL names a port that is not the scheme default.");
        }
    }

    private static void EnsureHostAllowed(Uri uri, MediaOptions options)
    {
        // Startup validation already refuses a malformed allow-list; failing closed here too
        // means a host that reached this code with one is denied rather than silently allowed.
        if (MediaOptions.FindMalformedAllowlistEntry(options.ImageUrlAllowlist) is not null)
        {
            throw Refuse(
                ImageUrlFetchReasons.AllowlistMalformed,
                "The configured image URL host allow-list is malformed.");
        }

        var entries = options.ImageUrlAllowlistEntries();
        if (entries.Count == 0)
        {
            return;
        }

        var host = uri.DnsSafeHost.TrimEnd('.');
        if (!entries.Any(entry => string.Equals(entry.TrimEnd('.'), host, StringComparison.OrdinalIgnoreCase)))
        {
            throw Refuse(
                ImageUrlFetchReasons.HostNotAllowed,
                "The image URL host is not on the configured allow-list.");
        }
    }

    /// <summary>
    /// Steps 10-11: an allow-listed content type, then the magic-byte sniff, then the streaming
    /// read under step 9's cap.
    /// </summary>
    private static async Task<FetchedImage> ReadImageAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var declared = response.Content.Headers.ContentType?.MediaType;
        if (!MediaContentTypes.IsImage(declared))
        {
            throw Refuse(
                ImageUrlFetchReasons.ContentTypeNotAllowed,
                "The image URL did not return an image content type.");
        }

        if (response.Content.Headers.ContentLength is { } declaredLength
            && declaredLength > MediaContentTypes.MaxFileBytes)
        {
            throw Refuse(
                ImageUrlFetchReasons.TooLarge,
                $"The image URL body exceeds the {MediaContentTypes.MaxFileBytes}-byte limit.");
        }

        var bytes = await ReadWithinCapAsync(response.Content, cancellationToken).ConfigureAwait(false);

        // The header was the sender's claim; the bytes are the truth. Without this, arbitrary
        // bytes travel into our Cloudinary account behind an image content type.
        var sniffed = MediaContentTypes.Sniff(bytes);
        if (sniffed is null || !MediaContentTypes.IsImage(sniffed))
        {
            throw Refuse(
                ImageUrlFetchReasons.ContentSignatureMismatch,
                "The image URL body is not a recognised image.");
        }

        // The sniffed type is returned, not the declared one: the bytes decided.
        return new FetchedImage(bytes, sniffed);
    }

    /// <summary>
    /// Reads the body under <see cref="MediaContentTypes.MaxFileBytes"/>, never asking for more
    /// than one byte past the cap, and aborting the moment that byte arrives. There is no
    /// unbounded read anywhere on this path.
    /// </summary>
    private static async Task<byte[]> ReadWithinCapAsync(
        HttpContent content, CancellationToken cancellationToken)
    {
        var cap = MediaContentTypes.MaxFileBytes;

        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[ReadChunkBytes];

        while (buffer.Length <= cap)
        {
            var remaining = cap + 1 - buffer.Length;
            var toRead = (int)Math.Min(chunk.Length, remaining);
            var read = await source
                .ReadAsync(chunk.AsMemory(0, toRead), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                return buffer.ToArray();
            }

            buffer.Write(chunk, 0, read);
        }

        // Exactly cap + 1 bytes were read and the body is still going: an honest refusal, never a
        // silent truncation.
        throw Refuse(
            ImageUrlFetchReasons.TooLarge,
            $"The image URL body exceeds the {MediaContentTypes.MaxFileBytes}-byte limit.");
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Found or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static Uri? ParseAbsolute(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    private static ImageUrlFetchException Refuse(string reason, string message)
        => new(reason, message);

    /// <summary>The host, and only the host: never the path or the query (item 13).</summary>
    private static string HostFor(Uri? uri)
    {
        if (uri is null)
        {
            return "(unparseable)";
        }

        try
        {
            return uri.DnsSafeHost;
        }
        catch (UriFormatException)
        {
            return "(unparseable)";
        }
    }

    /// <summary>
    /// A stable, non-reversible fingerprint of the full URL, so two log lines about the same URL
    /// correlate without the URL itself being present. Query strings can carry tokens.
    /// </summary>
    private static string UrlHash(string? value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)))
            [..16]
            .ToLowerInvariant();
}
