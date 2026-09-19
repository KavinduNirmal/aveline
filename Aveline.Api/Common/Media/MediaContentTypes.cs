namespace Aveline.Api.Common.Media;

/// <summary>
/// Content-type policy for stored binaries, shared by the catalog's product images and the
/// conversation thread's attachments (promoted out of
/// <c>Modules/VisualIntelligence/ImageContentTypes</c> so both read paths use one allow-list).
/// </summary>
/// <remarks>
/// Uploaders control the media type that accompanies a file, and the stored value is echoed
/// back by a serving endpoint. Without a policy an upload could be labelled <c>text/html</c>
/// and then rendered by a browser from the API origin, so every write path normalizes against
/// the allow-list and the read path re-checks the stored value.
/// </remarks>
public static class MediaContentTypes
{
    public const string DefaultImage = "image/jpeg";
    public const string Fallback = "application/octet-stream";
    public const string Pdf = "application/pdf";

    /// <summary>The per-file cap for a thread attachment: 5 MB.</summary>
    public const long MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>The per-message cap for a thread attachment: 5 files.</summary>
    public const int MaxPerMessage = 5;

    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
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

    /// <summary>Images plus PDF. Audio and video are deliberately excluded.</summary>
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
        "application/pdf",
    };

    /// <summary>Whether the declared media type is an image on the allow-list.</summary>
    public static bool IsImage(string? value)
        => MediaType(value) is { } mediaType && Images.Contains(mediaType);

    /// <summary>Whether the declared media type may be stored at all.</summary>
    public static bool IsAllowed(string? value)
        => MediaType(value) is { } mediaType && Allowed.Contains(mediaType);

    /// <summary>
    /// Constrains an uploader-supplied media type to the **image** allow-list, which is the
    /// catalog's rule. Returns <see cref="Fallback"/> for anything else.
    /// </summary>
    public static string NormalizeImage(string? declared)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            return DefaultImage;
        }

        return MediaType(declared) is { } mediaType && Images.Contains(mediaType)
            ? mediaType
            : Fallback;
    }

    /// <summary>
    /// Picks the content type used when serving stored bytes. Rows written before a policy
    /// existed, or with a type that has since been removed, are served as opaque bytes, so a
    /// serving endpoint can never return HTML or script.
    /// </summary>
    public static string SafeServe(string? stored)
        => MediaType(stored) is { } mediaType && Allowed.Contains(mediaType)
            ? mediaType
            : Fallback;

    /// <summary>
    /// The canonical content type to store, or <c>null</c> when the file may not be stored at
    /// all.
    /// </summary>
    /// <remarks>
    /// The declared type wins when it is on the allow-list. When it is absent or the generic
    /// <c>application/octet-stream</c> (which several clients send for a picked file), the file
    /// name's extension decides, so a legitimate upload is not rejected for a lazy header. A
    /// declared type that is present and disallowed is **not** rescued by an extension: the
    /// uploader said what it is, and the answer is no.
    /// </remarks>
    public static string? Resolve(string? declared, string? fileName)
    {
        var mediaType = MediaType(declared);
        if (mediaType is not null && Allowed.Contains(mediaType))
        {
            return mediaType.ToLowerInvariant();
        }

        if (mediaType is not null && !string.Equals(mediaType, Fallback, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".pdf" => Pdf,
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".avif" => "image/avif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            _ => null,
        };
    }

    private static string? MediaType(string? value)
    {
        var mediaType = value?.Split(';')[0].Trim();
        return string.IsNullOrEmpty(mediaType) ? null : mediaType;
    }
}
