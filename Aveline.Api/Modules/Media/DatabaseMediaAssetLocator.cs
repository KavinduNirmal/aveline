using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The production <see cref="IMediaAssetLocator"/>: it reads the two row types the protected tier
/// names, each scoped to the caller's organisation. No caller branches on the provider
/// (strategy §3.1) — the provider columns travel with the row.
/// </summary>
/// <remarks>
/// <para>
/// The token's key decides the lookup. A GUID key is the database tier's convention
/// (<c>DatabaseAttachmentStore</c> stores the row id, <c>DatabaseInventoryImageStore</c> stores
/// nothing), so it resolves by primary key; an encoded key is Cloudinary's, so it resolves by
/// <c>StorageKey</c>. Both branches carry the organisation in the predicate, which is what makes a
/// cross-org token a <c>404</c> rather than an asset.
/// </para>
/// <para>
/// The rows are read with <c>AsNoTracking</c>: this is a read path and the caller owns no changes.
/// </para>
/// </remarks>
public sealed class DatabaseMediaAssetLocator : IMediaAssetLocator
{
    private readonly AppDbContext _context;

    public DatabaseMediaAssetLocator(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<MediaAccessAsset?> FindByAssetKeyAsync(
        Guid organizationId, string assetKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetKey);

        if (Guid.TryParse(assetKey, out var id))
        {
            return await FindAttachmentByIdAsync(organizationId, id, ct).ConfigureAwait(false)
                   ?? await FindImageByIdAsync(organizationId, id, ct).ConfigureAwait(false);
        }

        var attachment = await _context.MessageAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.OrganizationId == organizationId && row.StorageKey == assetKey, ct)
            .ConfigureAwait(false);
        if (attachment is not null)
        {
            return FromAttachment(attachment);
        }

        var image = await _context.InventoryImages
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.OrgId == organizationId && row.StorageKey == assetKey, ct)
            .ConfigureAwait(false);

        return image is null ? null : FromImage(image);
    }

    /// <inheritdoc />
    public Task<MediaAccessAsset?> FindByReferenceAsync(
        Guid organizationId, string imageRefKind, Guid imageRefId, CancellationToken ct = default)
        => imageRefKind switch
        {
            MediaReferenceKinds.Attachment => FindAttachmentByIdAsync(organizationId, imageRefId, ct),
            MediaReferenceKinds.InventoryImage => FindImageByIdAsync(organizationId, imageRefId, ct),
            _ => Task.FromResult<MediaAccessAsset?>(null),
        };

    private async Task<MediaAccessAsset?> FindAttachmentByIdAsync(
        Guid organizationId, Guid attachmentId, CancellationToken ct)
    {
        var attachment = await _context.MessageAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.OrganizationId == organizationId && row.Id == attachmentId, ct)
            .ConfigureAwait(false);

        return attachment is null ? null : FromAttachment(attachment);
    }

    private async Task<MediaAccessAsset?> FindImageByIdAsync(
        Guid organizationId, Guid imageId, CancellationToken ct)
    {
        var image = await _context.InventoryImages
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.OrgId == organizationId && row.Id == imageId, ct)
            .ConfigureAwait(false);

        return image is null ? null : FromImage(image);
    }

    private static MediaAccessAsset FromAttachment(MessageAttachment attachment)
    {
        var assetKey = MediaStorageKey.AssetKey(attachment.StorageKey, attachment.Id);
        return new MediaAccessAsset(
            assetKey,
            MediaStorageKey.PublicId(assetKey),
            attachment.StorageProvider,
            attachment.StorageKey,
            attachment.ImageData,
            attachment.ContentType);
    }

    private static MediaAccessAsset FromImage(InventoryImage image)
    {
        var assetKey = MediaStorageKey.AssetKey(image.StorageKey, image.Id);
        return new MediaAccessAsset(
            assetKey,
            MediaStorageKey.PublicId(assetKey),
            image.StorageProvider,
            image.StorageKey,
            image.ImageData,
            image.ContentType);
    }
}
