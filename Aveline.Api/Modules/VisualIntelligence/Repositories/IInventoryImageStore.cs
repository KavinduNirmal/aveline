using Aveline.Api.Modules.VisualIntelligence.Models;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

/// <summary>What a catalog upload asked the store to keep.</summary>
public sealed record InventoryImageStoreRequest(
    Guid OrganizationId,
    Guid? ItemId,
    byte[] Bytes,
    string ContentType,
    string FileName,
    long SizeBytes);

/// <summary>
/// The byte boundary for a catalog image, deliberately small so swapping where the bytes live is
/// total.
/// </summary>
/// <remarks>
/// This is a <em>row</em> seam: it owns an <see cref="InventoryImage"/> row and delegates the
/// bytes to the provider seam <c>IMediaStorage</c>. <strong>No class implements two seams</strong>
/// (strategy §3.1) — the database store and the Cloudinary store each implement this interface
/// only, and neither knows a provider's public-id format.
/// </remarks>
public interface IInventoryImageStore
{
    /// <summary>The provider name stored on every row this store writes.</summary>
    string Provider { get; }

    /// <summary>Persists the bytes and returns the row describing them.</summary>
    Task<InventoryImage> StoreAsync(
        InventoryImageStoreRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// The stored bytes, or <c>null</c> when this provider keeps them elsewhere (a CDN adapter)
    /// or the row carries none.
    /// </summary>
    Task<Stream?> OpenReadAsync(
        InventoryImage image,
        CancellationToken ct = default);

    /// <summary>Removes the stored bytes. The caller removes the row.</summary>
    Task DeleteAsync(
        InventoryImage image,
        CancellationToken ct = default);
}
