using System.Collections.Concurrent;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The database tier's provider seam: the pass-through that keeps <see cref="IMediaStorage"/>
/// resolvable while the durable bytes live on the row (Postgres <c>bytea</c>), written and opened
/// by the row seams <c>DatabaseAttachmentStore</c> and <c>DatabaseInventoryImageStore</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the implementation used by the tests and by the <c>Media:Provider=database</c>
/// rollback (strategy §3.1, §5.2). It is a <em>provider</em> seam only: it owns no row type and
/// knows neither <c>InventoryImage</c> nor <c>MessageAttachment</c>, because no class implements
/// two seams.
/// </para>
/// <para>
/// The instance holds the bytes it was handed so the seam's put → open → delete contract is
/// exercisable in-process; that memory is not the database tier's durable copy, and a reference
/// this instance did not write reads as <c>null</c> — the seam's documented "the bytes live
/// elsewhere" answer (<c>IAttachmentStore.cs:22-26</c>). Because the resolved lifetime is scoped,
/// nothing is retained beyond the request that wrote it.
/// </para>
/// <para>
/// The database tier has no CDN origin, so <see cref="MediaPutRequest.PublicId"/> is the row's
/// serving location — the route-relative Aveline path the shipped routes already use, e.g.
/// <c>/api/v1/orgs/{orgId}/catalog/images/{imageId}</c>. It passes through unchanged as both the
/// storage key and the delivery URL.
/// </para>
/// </remarks>
public sealed class DatabaseMediaStorage : IMediaStorage
{
    /// <summary>The provider name written to every row this tier produces.</summary>
    public const string ProviderName = "database";

    private readonly ConcurrentDictionary<string, byte[]> _bytes = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public Task<StoredMedia> PutAsync(MediaPutRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Provider-neutral metadata has nowhere to go in a byte store, and ignoring it is
        // explicitly allowed by the record's doc comment (strategy §3.2).
        _bytes[request.PublicId] = request.Bytes;

        return Task.FromResult(new StoredMedia(ProviderName, request.PublicId, request.PublicId));
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(StoredMediaRef reference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return Task.FromResult<Stream?>(
            _bytes.TryGetValue(reference.StorageKey, out var bytes)
                ? new MemoryStream(bytes, writable: false)
                : null);
    }

    /// <inheritdoc />
    public Task DeleteAsync(StoredMediaRef reference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        _bytes.TryRemove(reference.StorageKey, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string PublicDeliveryUrl(StoredMediaRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return reference.StorageKey;
    }

    /// <inheritdoc />
    public string SignedDeliveryUrl(StoredMediaRef reference, DateTimeOffset expiresAtUtc)
    {
        // The database tier has no CDN to sign: the Aveline route is guarded by the API's own
        // authentication, so there is no separate expiring URL to mint.
        return PublicDeliveryUrl(reference);
    }
}
