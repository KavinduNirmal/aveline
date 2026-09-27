using System.Security.Cryptography;
using System.Text;
using Aveline.Api.Modules.Media;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// Supplies the public URL of a boutique's disclosure image, rendering and uploading it on first use.
/// </summary>
public interface IDisclosureImageProvider
{
    /// <summary>
    /// The public URL Meta should fetch for this boutique, or <c>null</c> when one cannot be produced.
    /// </summary>
    /// <remarks>
    /// Null is a normal answer, not a failure: the caller falls back to sending the notice as text.
    /// A missing image must degrade the message, never prevent it.
    /// </remarks>
    Task<string?> GetUrlAsync(
        Guid organizationId, string boutiqueDisplayName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Renders the disclosure image and publishes it to Cloudinary, then remembers the URL.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the public id carries a hash of the name.</b> Cloudinary delivery is immutable per public
/// id, so a boutique that renames itself would otherwise keep showing its old name on new
/// disclosures - a stale brand on a consent notice. Folding the name and the layout version into the
/// id means a rename (or a change to the drawing) produces a new id, and so a new URL, with nothing
/// to invalidate.
/// </para>
/// <para>
/// <b>Why the URL is derived rather than stored.</b> An uploaded asset's version-less URL resolves -
/// verified against this account - so the whole URL can be computed from the cloud name and the
/// public id. There is no second source of truth to keep in step with the first.
/// </para>
/// <para>
/// <b>Delivery is unsigned by design.</b> Meta fetches this URL with no credential of ours, so it
/// must not be a signed URL: a signed one would work at send time and fail when Meta got round to
/// fetching it. This is deliberately unlike the protected media tier, which is signed and expiring.
/// </para>
/// </remarks>
internal sealed class DisclosureImageProvider : IDisclosureImageProvider
{
    /// <summary>The Cloudinary folder every disclosure image lives under.</summary>
    public const string PublicIdRoot = "aveline/disclosure";

    private static readonly TimeSpan CacheFor = TimeSpan.FromDays(30);

    private readonly IDisclosureImageComposer _composer;
    private readonly ICloudinaryGateway? _cloudinary;
    private readonly IDistributedCache _cache;
    private readonly CloudinaryOptions _options;
    private readonly ILogger<DisclosureImageProvider> _logger;

    /// <param name="cloudinary">
    /// Optional on purpose. The Cloudinary gateway is registered only when the media module is
    /// configured for Cloudinary, and a deployment that uses the database media provider (or a test
    /// host) has none - which must degrade the disclosure to text, not stop the application from
    /// starting. That is exactly what happened when this was a required dependency: every
    /// integration test failed at host construction, not on the behaviour under test.
    /// </param>
    public DisclosureImageProvider(
        IDisclosureImageComposer composer,
        IDistributedCache cache,
        IOptions<CloudinaryOptions> options,
        ILogger<DisclosureImageProvider> logger,
        ICloudinaryGateway? cloudinary = null)
    {
        _composer = composer;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
        _cloudinary = cloudinary;
    }

    /// <inheritdoc />
    public async Task<string?> GetUrlAsync(
        Guid organizationId, string boutiqueDisplayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boutiqueDisplayName);

        var publicId = BuildPublicId(organizationId, boutiqueDisplayName, _composer.LayoutVersion);
        var cacheKey = $"privacy:disclosure-image:{publicId}";

        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        if (_cloudinary is null || string.IsNullOrWhiteSpace(_options.CloudName))
        {
            _logger.LogWarning(
                "No Cloudinary client or cloud name is configured, so no disclosure image can be "
                + "published. The notice will be sent as text. organizationId={OrganizationId}",
                organizationId);
            return null;
        }

        var bytes = _composer.Compose(boutiqueDisplayName);

        CloudinaryAttemptResult uploaded;
        using (var stream = new MemoryStream(bytes, writable: false))
        {
            uploaded = await _cloudinary.UploadImageAsync(
                new ImageUploadParams
                {
                    File = new FileDescription($"{publicId.Replace('/', '-')}.jpg", stream),
                    PublicId = publicId,
                    Overwrite = true,
                    Invalidate = true,
                },
                cancellationToken);
        }

        if (!uploaded.IsSuccess)
        {
            // Identifiers only. The provider's own error text can echo the request, and this one
            // carries no secret but is still not worth copying wholesale into our logs.
            _logger.LogWarning(
                "Disclosure image upload was refused. organizationId={OrganizationId} status={Status}",
                organizationId, uploaded.StatusCode);
            return null;
        }

        var url = BuildDeliveryUrl(_options.CloudName, publicId);
        await _cache.SetStringAsync(
            cacheKey, url, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheFor },
            cancellationToken);

        _logger.LogInformation(
            "Disclosure image published for organization {OrganizationId} ({Bytes} bytes).",
            organizationId, bytes.Length);

        return url;
    }

    /// <summary>
    /// <c>aveline/disclosure/{organizationId}-{hash}</c>, where the hash covers the name and the
    /// layout version so that either changing produces a new asset rather than a stale one.
    /// </summary>
    internal static string BuildPublicId(Guid organizationId, string boutiqueDisplayName, string layoutVersion)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{boutiqueDisplayName}\u0000{layoutVersion}"));
        var suffix = Convert.ToHexString(digest)[..12].ToLowerInvariant();
        return $"{PublicIdRoot}/{organizationId:N}-{suffix}";
    }

    /// <summary>
    /// The unsigned, version-less delivery URL. Verified to resolve for this account before the
    /// design depended on it, which is the only reason it is safe to construct rather than store.
    /// </summary>
    internal static string BuildDeliveryUrl(string cloudName, string publicId) =>
        $"https://res.cloudinary.com/{cloudName}/image/upload/{publicId}.jpg";
}
