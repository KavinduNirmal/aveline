using Aveline.Api.Common.Media;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The Cloudinary implementation of the provider seam <see cref="IMediaStorage"/>: it owns bytes,
/// and nothing else. It knows no row type — not <c>InventoryImage</c>, not
/// <c>MessageAttachment</c> — because no class implements two seams (strategy §3.1).
/// </summary>
/// <remarks>
/// <para>
/// The row seam hands in the deterministic public id the tagger built
/// (<c>aveline/{orgId}/catalog/{imageId}</c>), and this adapter uses it verbatim: the folder root
/// is already the key's first segment, so nothing is prefixed a second time.
/// </para>
/// <para>
/// <b><c>Overwrite = false</c> is the single most important line in the file.</b> A later
/// overwrite clears the tags and context this whole feature exists to set (risk R5), so a
/// re-upload of the same deterministic id answers with the existing asset instead of replacing it.
/// </para>
/// <para>
/// Metadata is applied <em>in the upload call</em> — labels become tags and attributes become the
/// <c>context</c> map — never in a second tagging pass, which would re-create an untagged window
/// (strategy §3.2, D1). <c>FileName</c> and <c>ContentType</c> never enter <c>context</c>.
/// </para>
/// </remarks>
internal sealed class CloudinaryMediaStorage : IMediaStorage
{
    /// <summary>The provider name written to every row this storage creates.</summary>
    public const string ProviderName = "cloudinary";

    /// <summary>How long the server-side private fetch URL is valid for.</summary>
    private static readonly TimeSpan PrivateFetchLifetime = TimeSpan.FromMinutes(5);

    private readonly ICloudinaryGateway _gateway;
    private readonly CloudinaryOptions _options;
    private readonly CloudinaryRetryPolicy _retry;

    public CloudinaryMediaStorage(
        ICloudinaryGateway gateway,
        IOptions<CloudinaryOptions> options,
        CloudinaryRetryPolicy retry,
        ILogger<CloudinaryMediaStorage> logger)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _retry = retry ?? throw new ArgumentNullException(nameof(retry));
        ArgumentNullException.ThrowIfNull(logger);
    }

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public async Task<StoredMedia> PutAsync(MediaPutRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A PDF is `Protected` with `kind:pdf` and `resource_type=raw` (strategy §3.2); the tier
        // is about who may read, not about format.
        var resourceType = IsPdf(request.ContentType)
            ? CloudinaryResourceTypes.Raw
            : CloudinaryResourceTypes.Image;
        var deliveryType = DeliveryTypeFor(request.Tier);

        var result = resourceType == CloudinaryResourceTypes.Raw
            ? await _retry.ExecuteAsync(
                "upload",
                token => _gateway.UploadRawAsync(BuildRawParameters(request, deliveryType), token),
                ct).ConfigureAwait(false)
            : await _retry.ExecuteAsync(
                "upload",
                token => _gateway.UploadImageAsync(BuildImageParameters(request, deliveryType), token),
                ct).ConfigureAwait(false);

        var url = result.SecureUrl ?? result.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new MediaStorageException(
                result.StatusCode,
                "Cloudinary accepted the upload but reported no delivery URL.");
        }

        // The provider's returned public id is authoritative: for a `raw` resource Cloudinary
        // appends the original file's extension to the requested id (verified live — a PDF asked
        // for as `…/{id}` is stored as `…/{id}.pdf`). The stored key must be what the delete and
        // fetch paths can resolve, so the returned value wins over the requested one.
        var storedPublicId = string.IsNullOrWhiteSpace(result.PublicId) ? request.PublicId : result.PublicId;

        return new StoredMedia(
            ProviderName,
            BuildStorageKey(resourceType, deliveryType, storedPublicId),
            url);
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(StoredMediaRef reference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var target = ParseStorageKey(reference.StorageKey);
        var url = IsProtected(target.DeliveryType)
            ? _gateway.BuildPrivateDownloadUrl(
                target.PublicId, target.ResourceType, target.DeliveryType, DateTimeOffset.UtcNow + PrivateFetchLifetime)
            : _gateway.BuildDeliveryUrl(target.PublicId, target.ResourceType, target.DeliveryType, signed: true);

        return await _gateway.FetchAsync(url, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(StoredMediaRef reference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var target = ParseStorageKey(reference.StorageKey);
        var parameters = new DeletionParams(target.PublicId)
        {
            ResourceType = CloudinaryResourceTypes.Parse(target.ResourceType),
            Type = target.DeliveryType,
            Invalidate = true,
        };

        _ = await _retry
            .ExecuteAsync("destroy", token => _gateway.DestroyAsync(parameters, token), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string PublicDeliveryUrl(StoredMediaRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var target = ParseStorageKey(reference.StorageKey);
        return _gateway.BuildDeliveryUrl(target.PublicId, target.ResourceType, target.DeliveryType, signed: false);
    }

    /// <inheritdoc />
    public string SignedDeliveryUrl(StoredMediaRef reference, DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var target = ParseStorageKey(reference.StorageKey);

        // An authenticated asset is fetched server-side through the API's expiring download URL;
        // anything else is a signed delivery URL (strategy §3.5).
        return IsProtected(target.DeliveryType)
            ? _gateway.BuildPrivateDownloadUrl(
                target.PublicId, target.ResourceType, target.DeliveryType, expiresAtUtc)
            : _gateway.BuildDeliveryUrl(target.PublicId, target.ResourceType, target.DeliveryType, signed: true);
    }

    private static ImageUploadParams BuildImageParameters(MediaPutRequest request, string deliveryType) => new()
    {
        File = FileDescriptionFor(request),
        PublicId = request.PublicId,
        Type = deliveryType,
        Overwrite = false,   // risk R5: an overwrite clears the tags and context.
        Invalidate = true,
        Tags = TagsFor(request.Metadata),
        Context = ContextFor(request.Metadata),
    };

    private static RawUploadParams BuildRawParameters(MediaPutRequest request, string deliveryType) => new()
    {
        File = FileDescriptionFor(request),
        PublicId = request.PublicId,
        Type = deliveryType,
        Overwrite = false,   // risk R5
        Invalidate = true,
        Tags = TagsFor(request.Metadata),
        Context = ContextFor(request.Metadata),
    };

    /// <summary>
    /// A fresh stream over the caller's array. The array is not owned by this adapter, so a
    /// dual-write row seam can still persist it (strategy §3.3).
    /// </summary>
    private static FileDescription FileDescriptionFor(MediaPutRequest request) =>
        new(request.FileName, new MemoryStream(request.Bytes, writable: false));

    /// <summary>Labels become the comma-separated tag list; no labels is no tags parameter.</summary>
    private static string? TagsFor(MediaMetadata? metadata) =>
        metadata is null || metadata.Labels.Count == 0
            ? null
            : string.Join(',', metadata.Labels);

    /// <summary>Attributes become the context map; no attributes is no context parameter.</summary>
    private static StringDictionary? ContextFor(MediaMetadata? metadata)
    {
        if (metadata is null || metadata.Attributes.Count == 0)
        {
            return null;
        }

        var context = new StringDictionary();
        foreach (var attribute in metadata.Attributes)
        {
            context.Add(attribute.Key, attribute.Value);
        }

        return context;
    }

    private string DeliveryTypeFor(MediaTier tier) => tier switch
    {
        MediaTier.Public => ValueOr(_options.CatalogDeliveryType, "upload"),
        MediaTier.Protected => ValueOr(_options.ProtectedDeliveryType, "authenticated"),
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown media tier."),
    };

    private static string ValueOr(string? configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured;

    private static bool IsProtected(string deliveryType) =>
        string.Equals(deliveryType, "authenticated", StringComparison.Ordinal);

    private static bool IsPdf(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return mediaType.Equals(MediaContentTypes.Pdf, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The encoded key: <c>{resourceType}/{deliveryType}:{publicId}</c> (strategy §3.3).</summary>
    private static string BuildStorageKey(string resourceType, string deliveryType, string publicId) =>
        $"{resourceType}/{deliveryType}:{publicId}";

    private static (string ResourceType, string DeliveryType, string PublicId) ParseStorageKey(string storageKey)
    {
        var separator = storageKey.IndexOf(':');
        if (separator <= 0 || separator == storageKey.Length - 1)
        {
            throw new MediaStorageException(
                $"The stored key '{storageKey}' is not a Cloudinary key of the form "
                + "'<resourceType>/<deliveryType>:<publicId>'.");
        }

        var prefix = storageKey[..separator];
        var publicId = storageKey[(separator + 1)..];

        var slash = prefix.IndexOf('/');
        if (slash <= 0 || slash == prefix.Length - 1)
        {
            throw new MediaStorageException(
                $"The stored key '{storageKey}' is missing its resource type or delivery type.");
        }

        var resourceType = prefix[..slash];
        var deliveryType = prefix[(slash + 1)..];

        if (resourceType is not (CloudinaryResourceTypes.Image or CloudinaryResourceTypes.Raw))
        {
            throw new MediaStorageException(
                $"The stored key '{storageKey}' names an unsupported resource type '{resourceType}'.");
        }

        return (resourceType, deliveryType, publicId);
    }
}
