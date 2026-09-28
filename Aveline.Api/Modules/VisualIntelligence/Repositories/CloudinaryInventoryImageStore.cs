using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

/// <summary>
/// The Cloudinary-tier catalog row seam (strategy §3.1, §3.3): it owns the
/// <see cref="InventoryImage"/> row, and the bytes belong to the provider seam
/// <see cref="IMediaStorage"/>.
/// </summary>
/// <remarks>
/// <para>
/// It implements <see cref="IInventoryImageStore"/> only (no class implements two seams) and
/// delegates the upload to the provider adapter. The only Cloudinary knowledge it has is the
/// deterministic catalog public id the tagger builds — <c>aveline/{orgId}/catalog/{imageId}</c> —
/// and the storage key the provider returned, which it persists verbatim rather than re-deriving
/// (unit U1.1's live finding: Cloudinary appends the format to a raw public id).
/// </para>
/// <para>
/// <b>Dual-write is this class's job, not the provider's.</b> The provider owns no row
/// (§3.1), so the second copy on the row is written here, <em>after</em> the store call: with
/// <c>Media:DualWrite</c> on, stage 1's rollback (<c>Media:Provider=database</c>) stays free.
/// With it off — the documented default — the row carries no bytes at all.
/// </para>
/// </remarks>
public sealed class CloudinaryInventoryImageStore : IInventoryImageStore
{
    /// <summary>The provider name written to every row this store creates.</summary>
    public const string ProviderName = "cloudinary";

    private readonly IMediaStorage _mediaStorage;
    private readonly IInventoryRepository _repository;
    private readonly MediaOptions _options;

    public CloudinaryInventoryImageStore(
        IMediaStorage mediaStorage,
        IInventoryRepository repository,
        IOptions<MediaOptions> options)
    {
        _mediaStorage = mediaStorage ?? throw new ArgumentNullException(nameof(mediaStorage));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    }

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public async Task<InventoryImage> StoreAsync(
        InventoryImageStoreRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var imageId = Guid.NewGuid();
        var publicId = MediaTagger.BuildCatalogPublicId(request.OrganizationId, imageId);
        var metadata = MediaTagger.ForCatalogImage(request.OrganizationId, DateTimeOffset.UtcNow);

        // The bytes go first: a row that names an asset the provider refused would be a dangling
        // reference. This is the same ordering contract the delete path keeps in reverse
        // (IAttachmentStore.cs:46, AttachmentSweepJob.cs:74-78).
        var stored = await _mediaStorage.PutAsync(
            new MediaPutRequest(
                publicId,
                request.Bytes,
                request.ContentType,
                request.FileName,
                MediaTier.Public,
                metadata),
            ct).ConfigureAwait(false);

        var image = new InventoryImage
        {
            Id = imageId,
            OrgId = request.OrganizationId,
            ItemId = request.ItemId,
            // The provider's own answers, not a local re-derivation.
            StorageProvider = stored.Provider,
            StorageKey = stored.StorageKey,
            // Dual-write's second copy (strategy §3.3; migration plan §6.4 step 6). Off by
            // default: keeping every byte in the database defeats the migration (§0.1).
            ImageData = _options.DualWrite ? request.Bytes : null,
            ContentType = request.ContentType,
            FileName = request.FileName,
            FileSizeBytes = request.SizeBytes,
            // The absolute secure CDN URL, carrying the one-width f_auto/q_auto contract.
            ImageUrl = CatalogDeliveryUrl.Build(stored, _options.CatalogDisplayWidth),
            CreatedAtUtc = DateTime.UtcNow,
        };

        await _repository.AddImageAsync(image, ct).ConfigureAwait(false);

        return image;
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(InventoryImage image, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        // The bytes live at the CDN; only a dual-write copy can be handed back from the row.
        var bytes = image.ImageData;
        return Task.FromResult<Stream?>(
            bytes is { Length: > 0 } ? new MemoryStream(bytes, writable: false) : null);
    }

    /// <inheritdoc />
    public Task DeleteAsync(InventoryImage image, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        return _mediaStorage.DeleteAsync(
            new StoredMediaRef(image.StorageProvider, image.StorageKey ?? string.Empty, image.ContentType),
            ct);
    }
}
