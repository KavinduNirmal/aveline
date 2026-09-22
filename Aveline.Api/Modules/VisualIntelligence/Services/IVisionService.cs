using System;
using System.Threading;
using System.Threading.Tasks;
using Aveline.Api.Modules.VisualIntelligence.DTOs;

namespace Aveline.Api.Modules.VisualIntelligence.Services;

public interface IVisionService
{
    /// <summary>
    /// Analyses the image named by <paramref name="imageUrl"/>. Exactly three target shapes are
    /// possible, and only the first two are usable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1. Inline bytes</b> - a <c>data:</c> URL (case-insensitive) whose media type, taken
    /// between <c>data:</c> and the first <c>;</c> or <c>,</c>, is one of the four the provider
    /// reads (<c>image/jpeg</c>, <c>image/png</c>, <c>image/gif</c>, <c>image/webp</c>) and whose
    /// base64 payload is non-empty. This is the correct shape when the caller holds the bytes: the
    /// provider fetches an <c>http(s)</c> URL itself, and media served through
    /// <c>MediaTokenMintService</c> is proxied by this API and is therefore an internal hostname
    /// (<c>http://api:8080</c>) that no external provider can resolve. Handing over the bytes
    /// removes the reachability requirement for every media provider.
    /// </para>
    /// <para>
    /// <b>2. Provider-fetchable</b> - an absolute URI whose scheme is <c>http</c> or <c>https</c>.
    /// The provider fetches it itself, exactly as before.
    /// </para>
    /// <para>
    /// <b>3. Anything else</b> - a relative path such as the Aveline media route
    /// <c>/api/v1/orgs/{orgId}/catalog/images/{id}</c> stored in <c>InventoryImages.ImageUrl</c>, a
    /// scheme-relative <c>//host/path</c>, a <c>blob:</c>/<c>file:</c> URL, or an unparseable value
    /// - is a <b>caller error</b>, not a provider failure. The provider answers
    /// <c>400 "Unsupported image_url format"</c> for it, so rather than spend a provider call and
    /// return the deterministic fallback (which derives attributes from the file name and hint text
    /// and reads no pixels), this method throws <see cref="ArgumentException"/> naming the problem.
    /// </para>
    /// <para>
    /// The deterministic fallback is unchanged for the honest failures: no configured provider key,
    /// a non-success provider status, an unparsable provider response, and a transport fault. The
    /// distinction is "the request named no readable image" (refused) versus "we tried and could
    /// not" (fallback).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The target is blank, is inline data of a type the provider cannot read or carrying no
    /// payload, or is neither an absolute <c>http(s)</c> URL nor inline image data.
    /// </exception>
    Task<ImageAnalysisResultDto> AnalyzeAsync(
        string imageUrl,
        Guid organizationId,
        string? fileName = null,
        string? contextHint = null,
        CancellationToken cancellationToken = default);
}
