using Aveline.Api.Common.Media;

namespace Aveline.Api.Modules.Conversations.Media;

/// <summary>
/// The validated result of a pasted-image-URL fetch: the bytes, and the canonical content type
/// the <em>bytes</em> were sniffed as (never the content type the peer declared).
/// </summary>
/// <remarks>
/// <para>
/// <b>The type carries the image invariant (F5).</b> A <see cref="FetchedImage"/> can only be
/// constructed with a content type on the image allow-list, so a caller cannot hold one whose
/// <see cref="ContentType"/> is a non-image and cannot silently store <c>application/pdf</c> or
/// <c>text/html</c> by forgetting a separate <see cref="MediaContentTypes.IsImage"/> check. The
/// validation lives at the only public construction path rather than in a factory, so the existing
/// positional construction sites keep working and no caller can bypass the guard.
/// </para>
/// <para>
/// The caller computes the byte-level identity with the same helper every other conversation
/// attachment uses (<c>AttachmentContentHash.Compute</c>) and passes both to the store, so the
/// fetched path and the uploaded path share one identity convention (strategy §9).
/// </para>
/// </remarks>
public sealed record FetchedImage
{
    /// <summary>
    /// Creates the fetched-image result, refusing any content type that is not an image on the
    /// shared allow-list. The bytes are the sniffed bytes and the content type is the sniffed
    /// canonical type; both come from <see cref="MediaContentTypes.Sniff"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="bytes"/> is null, or <paramref name="contentType"/> is null, blank or not an
    /// image on <see cref="MediaContentTypes.ImageTypes"/>.
    /// </exception>
    public FetchedImage(byte[] bytes, string contentType)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        // The invariant is enforced here because the fetcher's own guard is a separate statement;
        // a future caller must not be able to obtain a FetchedImage whose type is not an image.
        if (!MediaContentTypes.IsImage(contentType))
        {
            throw new ArgumentException(
                $"A fetched image must carry an image content type; '{contentType}' is not on the image allow-list.",
                nameof(contentType));
        }

        Bytes = bytes;
        ContentType = contentType;
    }

    /// <summary>The sniffed bytes. Their signature is what decided <see cref="ContentType"/>.</summary>
    public byte[] Bytes { get; }

    /// <summary>The canonical, sniffed image content type; never a declared header value.</summary>
    public string ContentType { get; }
}

/// <summary>
/// Fetches a caller-supplied image URL on the server's behalf, behind the SSRF guard specified
/// in the salon plan §7.5. Owned by unit U4.2; the only genuinely new attack surface in the
/// media workstream.
/// </summary>
/// <remarks>
/// <para>
/// The implementation validates before it connects, resolves the host itself, refuses any
/// resolution that contains a non-public address, pins the connection to the validated address,
/// follows redirects manually with a full re-validation on every hop, bounds the whole fetch by
/// a timeout budget, streams the body through a hard size cap, requires an allow-listed image
/// content type and re-confirms it with a magic-byte sniff.
/// </para>
/// <para>
/// <b>Failure discipline.</b> A refusal is a typed <see cref="ImageUrlFetchException"/> carrying
/// a caller-safe <see cref="ImageUrlFetchException.Reason"/> and a message that contains no part
/// of the input URL. The fetcher logs at <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/>
/// and never logs the full URL — only the host and a hash. The caller is expected to catch the
/// refusal, log nothing more, leave the message text intact, and add no attachment
/// (salon §7.5 items 13 and 14; the webhook's established discipline at
/// <c>WebhookEndpoints.cs:346-358</c>). The fetcher therefore never fails the send by throwing
/// anything other than <see cref="ImageUrlFetchException"/>; the only exception that escapes
/// untouched is the caller's own cancellation.
/// </para>
/// </remarks>
public interface IImageUrlFetcher
{
    /// <summary>
    /// Validates and retrieves <paramref name="imageUrl"/>, or throws
    /// <see cref="ImageUrlFetchException"/> with a reason the caller can map to a response.
    /// </summary>
    /// <param name="imageUrl">The caller-supplied absolute URL. Never echoed into a log line.</param>
    /// <param name="cancellationToken">The caller's request cancellation.</param>
    Task<FetchedImage> FetchAsync(string? imageUrl, CancellationToken cancellationToken);
}
