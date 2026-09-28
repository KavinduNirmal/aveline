namespace Aveline.Api.Modules.Media;

/// <summary>
/// Where bytes live: the provider seam. One implementation per provider, selected by
/// <c>Media:Provider</c>; the row seams delegate here. <strong>No caller branches on the
/// provider</strong> (strategy §3.1) — the implementation is chosen once, in DI.
/// </summary>
public interface IMediaStorage
{
    /// <summary>The provider name written to every row this storage creates.</summary>
    string Provider { get; }

    /// <summary>Stores the bytes and returns the reference to persist on the row.</summary>
    Task<StoredMedia> PutAsync(MediaPutRequest request, CancellationToken ct = default);

    /// <summary>
    /// The bytes, or <c>null</c> when this provider keeps them elsewhere. Null is not an error:
    /// it is how the caller learns to use the stored URL instead.
    /// </summary>
    Task<Stream?> OpenReadAsync(StoredMediaRef reference, CancellationToken ct = default);

    /// <summary>Removes the stored bytes. The caller removes the row.</summary>
    Task DeleteAsync(StoredMediaRef reference, CancellationToken ct = default);

    /// <summary>The public, permanent delivery URL. Only valid for the public tier.</summary>
    string PublicDeliveryUrl(StoredMediaRef reference);

    /// <summary>An expiring URL for a private asset, fetched server-side and never handed out.</summary>
    string SignedDeliveryUrl(StoredMediaRef reference, DateTimeOffset expiresAtUtc);
}
