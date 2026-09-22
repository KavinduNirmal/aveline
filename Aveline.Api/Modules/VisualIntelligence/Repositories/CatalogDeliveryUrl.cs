using Aveline.Api.Modules.Media;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

/// <summary>
/// The catalog tier's delivery contract (strategy §3.7): one width, <c>f_auto</c> and
/// <c>q_auto</c>, applied to the provider's own absolute URL.
/// </summary>
/// <remarks>
/// <para>
/// <b>Exactly one width, structurally.</b> <see cref="Build(string, string, int)"/> renders a
/// single <c>w_{displayWidth},f_auto,q_auto</c> segment from one scalar width, so there is no
/// code path here that can append a second <c>w_</c>. That matters more than it reads: each
/// additional width multiplies <c>f_auto</c>'s derivations, and three widths times two formats is
/// six derivations per asset — 30 credits at only 5,000 assets — which loses the Free plan faster
/// than the bandwidth it saves (strategy §3.7, §6 item 12).
/// </para>
/// <para>
/// <c>f_auto</c>, never a fixed format. Cloudinary's automatic quality algorithm refuses WebP
/// when chroma subsampling would hurt colour fidelity, which is exactly the boutique-colour case
/// the vision pipeline samples a hex from, so forcing <c>f_webp</c> would trade that fidelity for
/// bytes (strategy §3.7 point 2).
/// </para>
/// <para>
/// The transformation goes immediately after the delivery-type segment the storage key names
/// (<c>image/upload:</c> → <c>/image/upload/</c>), before any version. The key is authoritative
/// rather than a hard-coded <c>upload</c>, so a configured catalog delivery type is honoured.
/// </para>
/// </remarks>
public static class CatalogDeliveryUrl
{
    /// <summary>The automatic-format token; a fixed <c>f_&lt;fmt&gt;</c> is never emitted.</summary>
    public const string AutoFormat = "f_auto";

    /// <summary>The automatic-quality token; a fixed <c>q_&lt;n&gt;</c> is never emitted.</summary>
    public const string AutoQuality = "q_auto";

    /// <summary>The delivery URL for a provider result, on the row's own storage key.</summary>
    public static string Build(StoredMedia stored, int displayWidth)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return Build(stored.Url, stored.StorageKey, displayWidth);
    }

    /// <summary>The provider's absolute URL, carrying the one-width delivery transformation.</summary>
    /// <param name="providerUrl">The absolute URL the provider returned for the asset.</param>
    /// <param name="storageKey">The row's encoded key, <c>{resourceType}/{deliveryType}:{publicId}</c>.</param>
    /// <param name="displayWidth">The one configured width (<c>Media:CatalogDisplayWidth</c>).</param>
    public static string Build(string providerUrl, string storageKey, int displayWidth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        // A variant of zero width is a bug, not a default (strategy §3.4).
        if (displayWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(displayWidth),
                displayWidth,
                "Media:CatalogDisplayWidth must be greater than zero; a zero-width variant is a bug.");
        }

        var marker = DeliveryMarker(storageKey);
        var markerStart = providerUrl.IndexOf(marker, StringComparison.Ordinal);
        if (markerStart < 0)
        {
            throw new InvalidOperationException(
                $"The provider URL '{providerUrl}' does not carry the delivery segment '{marker}' "
                + "named by the storage key, so the catalog transformation has nowhere to go.");
        }

        // One width in, one `w_` out. There is no second-width parameter to pass.
        var transformation = $"w_{displayWidth},{AutoFormat},{AutoQuality}";
        return providerUrl.Insert(markerStart + marker.Length, transformation + "/");
    }

    /// <summary>The <c>/image/upload/</c> style segment the transformation follows.</summary>
    private static string DeliveryMarker(string storageKey)
    {
        var separator = storageKey.IndexOf(':');
        var prefix = separator > 0 ? storageKey[..separator] : storageKey;
        return $"/{prefix}/";
    }
}
