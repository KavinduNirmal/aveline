using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.VisualIntelligence.Models;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

/// <summary>
/// The database-tier catalog row seam: the bytes sit in the row itself (Postgres <c>bytea</c>),
/// following the shipped precedent for message attachments.
/// </summary>
/// <remarks>
/// It implements <see cref="IInventoryImageStore"/> only and delegates the bytes to the provider
/// seam <see cref="IMediaStorage"/> (strategy §3.1: no class implements two seams). It therefore
/// never learns a provider's public-id format. The reserved <c>StorageProvider</c>/<c>StorageKey</c>
/// columns are what keep the swap to a CDN adapter to a new adapter plus a config value.
/// </remarks>
public sealed class DatabaseInventoryImageStore : IInventoryImageStore
{
    private readonly AppDbContext _context;
    private readonly IMediaStorage _mediaStorage;

    public DatabaseInventoryImageStore(AppDbContext context, IMediaStorage mediaStorage)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _mediaStorage = mediaStorage ?? throw new ArgumentNullException(nameof(mediaStorage));
    }

    public string Provider => "database";

    public async Task<InventoryImage> StoreAsync(
        InventoryImageStoreRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var imageId = Guid.NewGuid();
        var image = new InventoryImage
        {
            Id = imageId,
            OrgId = request.OrganizationId,
            ItemId = request.ItemId,
            StorageProvider = Provider,
            ImageData = request.Bytes,
            ContentType = request.ContentType,
            FileName = request.FileName,
            FileSizeBytes = request.SizeBytes,
            // The database tier's delivery route is the relative API path; the Cloudinary tier
            // stores its absolute CDN URL instead (U1.2).
            ImageUrl = $"/api/v1/orgs/{request.OrganizationId}/catalog/images/{imageId}",
            CreatedAtUtc = DateTime.UtcNow,
        };

        _context.InventoryImages.Add(image);
        await _context.SaveChangesAsync(ct);

        return image;
    }

    public Task<Stream?> OpenReadAsync(InventoryImage image, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var bytes = image.ImageData;
        if (bytes is null || bytes.Length == 0)
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
    }

    public Task DeleteAsync(InventoryImage image, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        // The bytes live in the row, so the caller's row delete is the delete; telling the provider
        // first is the seam's ordering contract (IAttachmentStore.cs:46), and it is what makes a
        // CDN adapter's release happen before the row that names it disappears.
        return _mediaStorage.DeleteAsync(
            new StoredMediaRef(image.StorageProvider, image.StorageKey ?? string.Empty, image.ContentType),
            ct);
    }
}
