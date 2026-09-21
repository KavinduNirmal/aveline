using System.Collections.Frozen;

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
/// <para>
/// The allow-list is a statement about what may be <em>stored</em>, not about what any consumer
/// can process. A consumer with a narrower capability - the vision provider, which reads four
/// formats - declares its own subset over this list; see <see cref="VisionContentTypes"/>.
/// </para>
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

    /// <summary>The nine image types this policy admits, in wire order. The one source of truth.</summary>
    private static readonly string[] ImageTypeList =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "image/avif",
        "image/bmp",
        "image/tiff",
        "image/heic",
        "image/heif",
    ];

    private static readonly FrozenSet<string> Images =
        ImageTypeList.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Images plus PDF. Audio and video are deliberately excluded.</summary>
    private static readonly FrozenSet<string> Allowed =
        ImageTypeList.Append(Pdf).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The image allow-list, exposed read-only so a consumer with a narrower capability can
    /// assert its own subset against the real collection rather than against a copied list
    /// (strategy §3.6). Membership must be tested with the returned set's own comparer, which
    /// is case-insensitive.
    /// </summary>
    public static IReadOnlySet<string> ImageTypes => Images;

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
    /// <para>
    /// This method trusts the declared type and, failing that, the file name; it never reads the
    /// bytes, and this behaviour is deliberately unchanged. A caller that needs the bytes to
    /// decide calls <see cref="Sniff"/> **as well** and opts in per call site (strategy §3.6).
    /// </para>
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

    // -----------------------------------------------------------------------------------------
    // The magic-byte sniff
    // -----------------------------------------------------------------------------------------

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The literal PDF header marker. A real header adds version digits after it.</summary>
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    /// <summary>
    /// How far into the payload a PDF header may start and still be accepted. A well-formed PDF
    /// places <c>%PDF-</c> at byte 0; a small run of leading bytes is a common malformation, so a
    /// bounded window tolerates it. The bound is what keeps this a header check rather than a
    /// substring search: a marker buried deeper in the payload is not a header.
    /// </summary>
    private const int PdfSignatureWindow = 1024;

    /// <summary>An ISO base-media-file box: a 4-byte size, then the <c>ftyp</c> marker and a brand.</summary>
    private const int FtypBrandOffset = 8;

    private const int FtypBrandLength = 4;

    /// <summary>
    /// Identifies a stored file's real type from its leading bytes, ignoring any declared content
    /// type or file name, and returns the canonical type or <c>null</c> when the bytes are not a
    /// known image or PDF.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This closes the gap <see cref="Resolve"/> leaves open: because a generic
    /// <c>application/octet-stream</c> is rescued from the file extension, a <c>.png</c> claimed
    /// over an HTML body is admitted today. The sniff reads the signature instead, so a
    /// JPEG-claimed HTML body is refused. It is also what makes
    /// <see cref="VisionContentTypes"/> meaningful: the vision provider detects format from the
    /// bytes, so we must too (strategy §3.6).
    /// </para>
    /// <para>
    /// The recognised sets are the image allow-list plus <c>application/pdf</c>. The PDF arm
    /// exists so a body that claims <c>application/pdf</c> can be confirmed by its bytes rather
    /// than trusted; a PDF is the most hostile arm the pipeline stores, because a PDF document
    /// can carry JavaScript. Every type returned is a member of
    /// <see cref="Allowed"/>; the image members are exactly <see cref="ImageTypes"/>.
    /// </para>
    /// <para>
    /// The method is pure and I/O-free: it reads only the array it is handed, allocates nothing
    /// beyond a possible substring, and mutates nothing. Every type it can return is a member of
    /// <see cref="Allowed"/>, so the sniff and the storage allow-list cannot drift apart.
    /// </para>
    /// <para>
    /// Opting in is per call site. This method does not change <see cref="Resolve"/>, and a
    /// caller that does not need it is unaffected.
    /// </para>
    /// </remarks>
    /// <param name="bytes">The leading bytes of an upload. A prefix is enough; <c>null</c> is refused.</param>
    /// <returns>
    /// The canonical image or PDF type, or <c>null</c> when the bytes carry no known signature.
    /// </returns>
    public static string? Sniff(byte[]? bytes)
    {
        if (bytes is null)
        {
            return null;
        }

        var head = bytes.AsSpan();

        // PDF first, in its own leading window. A hostile body cannot reach a later image branch
        // by prefixing a `%PDF-` marker, and the bounded window refuses a marker buried in the
        // payload.
        if (SniffPdf(head) is { } pdf)
        {
            return pdf;
        }

        // JPEG: SOI marker followed by any marker.
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (head.Length >= PngSignature.Length && head[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return "image/png";
        }

        // GIF87a and GIF89a differ in the version digits; the family is the first four bytes.
        if (head.Length >= 4 && head[0] == (byte)'G' && head[1] == (byte)'I'
            && head[2] == (byte)'F' && head[3] == (byte)'8')
        {
            return "image/gif";
        }

        // RIFF carries several container formats; the WEBP fourcc at offset 8 makes it an image.
        if (head.Length >= 12
            && head[0] == (byte)'R' && head[1] == (byte)'I'
            && head[2] == (byte)'F' && head[3] == (byte)'F'
            && head.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        // Windows bitmap.
        if (head.Length >= 2 && head[0] == (byte)'B' && head[1] == (byte)'M')
        {
            return "image/bmp";
        }

        // TIFF, both byte orders: "II" little-endian, "MM" big-endian, then the 42 magic.
        if (head.Length >= 4
            && ((head[0] == (byte)'I' && head[1] == (byte)'I' && head[2] == 0x2A && head[3] == 0x00)
                || (head[0] == (byte)'M' && head[1] == (byte)'M' && head[2] == 0x00 && head[3] == 0x2A)))
        {
            return "image/tiff";
        }

        return SniffIsoBrand(head);
    }

    /// <summary>
    /// Whether <see cref="Sniff"/> recognises the bytes as a **storable** file. This includes
    /// <c>application/pdf</c>, which is storable but is not an image; use
    /// <see cref="IsImage"/> on the sniffed type when the image question is the one being asked.
    /// </summary>
    /// <param name="bytes">The leading bytes of an upload.</param>
    public static bool IsRecognisableImage(byte[]? bytes)
        => Sniff(bytes) is { } sniffed && IsImage(sniffed);

    /// <summary>
    /// Finds the PDF header inside its bounded leading window and requires at least one version
    /// character after the marker, so a truncated <c>%PDF-</c> is not a document.
    /// </summary>
    private static string? SniffPdf(ReadOnlySpan<byte> head)
    {
        // At least one byte of version must follow the five-byte marker.
        const int required = 6;

        var lastStart = Math.Min(PdfSignatureWindow, head.Length - required);
        for (var offset = 0; offset <= lastStart; offset++)
        {
            if (head.Slice(offset, PdfSignature.Length).SequenceEqual(PdfSignature))
            {
                return Pdf;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the ISO base-media-file <c>ftyp</c> brand. The brand is the one format claim a
    /// container we do not otherwise parse makes, and it is what separates an AVIF or a HEIC
    /// from an MP4 that shares the box layout.
    /// </summary>
    private static string? SniffIsoBrand(ReadOnlySpan<byte> head)
    {
        if (head.Length < FtypBrandOffset + FtypBrandLength
            || !head.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return null;
        }

        return BrandToUInt32(head.Slice(FtypBrandOffset, FtypBrandLength)) switch
        {
            // avif, avis: the AV1 image file format and its sequence variant.
            0x61766966 or 0x61766973 => "image/avif",

            // heic, heix, heim, heis, hevc, hevx: the HEIF structural brand family, all of
            // which the vision provider refuses and all of which store as image/heic.
            0x68656963 or 0x68656978 or 0x6865696D or 0x68656973
                or 0x68657663 or 0x68657678 => "image/heic",

            // mif1, msf1: a generic HEIF container stores as image/heif.
            0x6D696631 or 0x6D736631 => "image/heif",

            _ => null,
        };
    }

    /// <summary>
    /// Packs exactly four ASCII brand bytes into a value so the brand switch reads as the brand
    /// itself rather than as an offset computation. Returns zero for a short or non-ASCII run,
    /// which never matches a branch.
    /// </summary>
    private static uint BrandToUInt32(ReadOnlySpan<byte> brand)
    {
        if (brand.Length != FtypBrandLength)
        {
            return 0;
        }

        foreach (var value in brand)
        {
            if (value is < (byte)' ' or > (byte)'~')
            {
                return 0;
            }
        }

        return ((uint)brand[0] << 24)
            | ((uint)brand[1] << 16)
            | ((uint)brand[2] << 8)
            | brand[3];
    }

    private static string? MediaType(string? value)
    {
        var mediaType = value?.Split(';')[0].Trim();
        return string.IsNullOrEmpty(mediaType) ? null : mediaType;
    }
}
