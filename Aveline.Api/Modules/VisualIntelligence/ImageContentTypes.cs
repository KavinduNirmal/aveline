namespace Aveline.Api.Modules.VisualIntelligence;

/// <summary>
/// Content-type policy for stored image binaries.
/// </summary>
/// <remarks>
/// Uploaders control the media type that accompanies an image, and the stored value is echoed
/// back by the anonymous image endpoint. Without a policy an upload could be labelled
/// <c>text/html</c> and then rendered by a browser from the API origin, so every write path
/// normalizes against the allow-list and the read path re-checks the stored value.
/// </remarks>
internal static class ImageContentTypes
{
    internal const string DefaultImage = "image/jpeg";
    internal const string Fallback = "application/octet-stream";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "image/avif",
        "image/bmp",
        "image/tiff",
        "image/heic",
        "image/heif",
    };

    /// <summary>
    /// Constrains an uploader-supplied media type to the image allow-list before it is stored.
    /// </summary>
    internal static string Normalize(string? declared)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            return DefaultImage;
        }

        return MediaType(declared) is { } mediaType && Allowed.Contains(mediaType)
            ? mediaType
            : Fallback;
    }

    /// <summary>
    /// Picks the content type used when serving a stored image. Rows written before this policy
    /// existed are still served as opaque bytes, so the endpoint can never return HTML or script.
    /// </summary>
    internal static string SafeServe(string? stored)
    {
        return MediaType(stored) is { } mediaType && Allowed.Contains(mediaType)
            ? mediaType
            : Fallback;
    }

    private static string? MediaType(string? value)
    {
        var mediaType = value?.Split(';')[0].Trim();
        return string.IsNullOrEmpty(mediaType) ? null : mediaType;
    }
}
