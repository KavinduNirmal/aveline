namespace Aveline.Api.Modules.Conversations.Media;

/// <summary>
/// The validated result of a pasted-image-URL fetch: the bytes, and the canonical content type
/// the <em>bytes</em> were sniffed as (never the content type the peer declared).
/// </summary>
/// <remarks>
/// The caller computes the byte-level identity with the same helper every other conversation
/// attachment uses (<c>AttachmentContentHash.Compute</c>) and passes both to the store, so the
/// fetched path and the uploaded path share one identity convention (strategy §9).
/// </remarks>
public sealed record FetchedImage(byte[] Bytes, string ContentType);

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
