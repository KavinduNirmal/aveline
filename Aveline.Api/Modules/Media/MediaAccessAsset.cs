namespace Aveline.Api.Modules.Media;

/// <summary>
/// A row resolved for a verified token, enough to apply the per-row dispatch of strategy §3.3.
/// </summary>
/// <param name="AssetKey">The token's <c>p</c> claim: the row's own storage key, or the row id.</param>
/// <param name="PublicId">
/// The Cloudinary public id inside <paramref name="AssetKey"/> (or the key unchanged when it is not
/// an encoded Cloudinary key). Returned to a mint caller so the analysis cache can key on
/// <c>{orgId}:{publicId}</c> (migration plan §7.6).
/// </param>
/// <param name="StorageProvider">The provider that wrote the row, e.g. <c>cloudinary</c>.</param>
/// <param name="StorageKey">The provider's own key, when the row has one.</param>
/// <param name="ImageData">The database-tier bytes, when the row has them (dual-write).</param>
/// <param name="ContentType">The stored content type, re-checked before it is served.</param>
public sealed record MediaAccessAsset(
    string AssetKey,
    string PublicId,
    string? StorageProvider,
    string? StorageKey,
    byte[]? ImageData,
    string ContentType);

/// <summary>What a served protected request hands back: the bytes and the type to serve them as.</summary>
public sealed record MediaAccessGrant(string AssetKey, string PublicId, string ContentType, Stream Content);

/// <summary>The outcome of <see cref="MediaAccessService.OpenAsync"/>.</summary>
/// <param name="Failure">Why the request was refused; <see cref="MediaAccessFailure.None"/> on success.</param>
/// <param name="ProviderDetail">The provider's own error text, for the log — never for a response body.</param>
/// <param name="Grant">The served asset, on success.</param>
public sealed record MediaAccessResult(
    MediaAccessFailure Failure,
    string? ProviderDetail = null,
    MediaAccessGrant? Grant = null)
{
    /// <summary>Whether the asset was served.</summary>
    public bool IsSuccess => Failure == MediaAccessFailure.None && Grant is not null;

    /// <summary>A served asset.</summary>
    public static MediaAccessResult Served(MediaAccessGrant grant) =>
        new(MediaAccessFailure.None, null, grant);

    /// <summary>A refusal, optionally carrying the provider's detail for the log.</summary>
    public static MediaAccessResult Refused(MediaAccessFailure failure, string? providerDetail = null) =>
        new(failure, providerDetail);
}
