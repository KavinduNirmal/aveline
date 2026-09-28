using System.Collections.Frozen;

namespace Aveline.Api.Common.Media;

/// <summary>
/// The subset of <see cref="MediaContentTypes.Images"/> that the vision provider can actually
/// read, and the policy entry point the vision path validates against before it mints a URL
/// (strategy §3.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a subset exists at all.</b> The storage allow-list and the provider's capability are
/// different questions, and conflating them produces a specific, confusing defect. Nine image
/// types may be stored; the vision provider reads exactly four - JPEG, PNG, GIF and WebP - and
/// detects the format from the bytes rather than from the declared type. A HEIC photograph is
/// perfectly valid in a conversation thread, so it must stay storable, servable and tagged
/// normally. But if the pipeline handed it to the provider, the fetch would fail and the user
/// would see an image that renders fine in the thread and is inexplicably unanalysable, with no
/// error that names the cause. This type is where that distinction is recorded.
/// </para>
/// <para>
/// <b>The contract for a stored type outside the subset.</b> It is stored, served and tagged
/// exactly as before, and it is <em>recorded as not analysable</em>. It is never silently
/// degraded to a format the provider accepts, and it is never blocked at upload. That is the
/// module's established discipline: best-effort, logged, and never failing the user's action.
/// </para>
/// <para>
/// <b>The set relation is enforced, not documented.</b> Every type here is a member of
/// <see cref="MediaContentTypes.Images"/>, and the image list holds at least one type that is
/// not here (HEIC, AVIF, BMP, TIFF and HEIF, at the time of writing). The relation is asserted
/// against the real <see cref="MediaContentTypes.Images"/> collection by the unit tests, so the
/// two cannot drift apart silently when a format is added to one of them.
/// </para>
/// <para>
/// The method name is deliberate: it takes a <em>sniffed</em> type. Pass the result of
/// <see cref="MediaContentTypes.Sniff"/> rather than a client-supplied header, because the
/// provider's own decision is byte-based and ours must be too.
/// </para>
/// </remarks>
public static class VisionContentTypes
{
    /// <summary>
    /// The four formats the vision provider supports: JPEG, PNG, GIF and WebP. A strict subset
    /// of <see cref="MediaContentTypes.Images"/>, which admits nine.
    /// </summary>
    private static readonly FrozenSet<string> Analysable = new[]
    {
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The analysable subset, exposed read-only so a test or a caller can assert the strict
    /// subset relation against <see cref="MediaContentTypes.Images"/> rather than against a
    /// copied list. Membership must be tested with the returned set's own comparer, which is
    /// case-insensitive.
    /// </summary>
    public static IReadOnlySet<string> AnalysableTypes => Analysable;

    /// <summary>
    /// Whether the vision provider can analyse a stored image of this type.
    /// </summary>
    /// <param name="sniffedType">
    /// The type produced by <see cref="MediaContentTypes.Sniff"/>. A declared client header is
    /// not a substitute: the provider detects format from the bytes. Parameters after a
    /// semicolon and casing are tolerated, matching the rest of <see cref="MediaContentTypes"/>.
    /// </param>
    /// <returns>
    /// <c>true</c> only for JPEG, PNG, GIF and WebP. <c>false</c> for a storable type outside the
    /// subset (HEIC, AVIF, BMP, TIFF, HEIF), for a non-image, and for <c>null</c> or blank - in
    /// every one of which cases the caller records the asset as not analysable and continues.
    /// </returns>
    public static bool IsAnalysable(string? sniffedType)
        => MediaType(sniffedType) is { } mediaType && Analysable.Contains(mediaType);

    private static string? MediaType(string? value)
    {
        var mediaType = value?.Split(';')[0].Trim();
        return string.IsNullOrEmpty(mediaType) ? null : mediaType;
    }
}
