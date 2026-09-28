namespace Aveline.Api.Modules.Media;

/// <summary>
/// The two spellings of an asset key, in one place. A token's <c>p</c> claim is the row's own
/// storage key when it has one, and the row id otherwise: the database adapters keep the id as the
/// key, so both providers resolve through the same lookup.
/// </summary>
public static class MediaStorageKey
{
    /// <summary>The key a token names for a row: its storage key, or its id when it has none.</summary>
    public static string AssetKey(string? storageKey, Guid id) =>
        string.IsNullOrWhiteSpace(storageKey) ? id.ToString() : storageKey;

    /// <summary>
    /// The Cloudinary public id inside an encoded key of the form
    /// <c>{resourceType}/{deliveryType}:{publicId}</c> (strategy §3.3). A key that is not encoded
    /// is returned unchanged, which is what the database tier's id-shaped keys need.
    /// </summary>
    public static string PublicId(string assetKey)
    {
        if (string.IsNullOrEmpty(assetKey))
        {
            return assetKey;
        }

        var separator = assetKey.IndexOf(':');
        return separator > 0 && separator < assetKey.Length - 1
            ? assetKey[(separator + 1)..]
            : assetKey;
    }
}
