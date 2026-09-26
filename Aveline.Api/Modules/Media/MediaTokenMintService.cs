using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Media;

/// <summary>Whether a mint produced a token.</summary>
public enum MediaTokenMintOutcome
{
    /// <summary>A token was minted.</summary>
    Minted,

    /// <summary>The referenced row does not exist for this organisation; no token was minted.</summary>
    NotFound,
}

/// <summary>
/// A mint's answer. <see cref="PublicId"/> is returned alongside the token because the agent's
/// analysis cache must key on <c>{orgId}:{publicId}</c> and cannot reconstruct it from a tokenised
/// URL (migration plan §7.6).
/// </summary>
public sealed record MediaTokenMintResult(
    MediaTokenMintOutcome Outcome,
    string? Token,
    string? Url,
    string? PublicId,
    DateTimeOffset ExpiresAtUtc)
{
    /// <summary>Whether a token was minted.</summary>
    public bool IsSuccess => Outcome == MediaTokenMintOutcome.Minted;

    /// <summary>A refusal: the row was not visible to this organisation.</summary>
    public static MediaTokenMintResult NotFound() =>
        new(MediaTokenMintOutcome.NotFound, null, null, null, default);
}

/// <summary>
/// Mints protected-tier tokens. The route owns the policy and the tenant re-check; this service
/// owns the request shape, the URL, and the TTL policy that the signer enforces.
/// </summary>
/// <remarks>
/// Minting is a local HMAC computation — there is nothing to cache, and a token cache would be a
/// new way for a stale credential to outlive its row (migration plan §7.6). A requested TTL of
/// <see cref="TimeSpan.Zero"/> means "the configured default", which the signer then bounds by the
/// scope's hard cap.
/// </remarks>
public sealed class MediaTokenMintService
{
    private readonly IMediaUrlSigner _signer;
    private readonly IMediaAssetLocator _locator;
    private readonly MediaOptions _options;

    public MediaTokenMintService(
        IMediaUrlSigner signer,
        IMediaAssetLocator locator,
        IOptions<MediaOptions> options)
    {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    }

    /// <summary>
    /// Mints a token for an asset key the caller has already resolved and tenant-checked.
    /// </summary>
    public MediaTokenMintResult Mint(Guid organizationId, string assetKey, MediaScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetKey);

        var request = new MediaTokenRequest(
            organizationId,
            assetKey,
            scope,
            Ttl: TimeSpan.Zero,
            SingleUse: scope == MediaScope.VisionAnalyze);

        var token = _signer.Mint(request, out var expiresAtUtc);

        return new MediaTokenMintResult(
            MediaTokenMintOutcome.Minted,
            token,
            BuildUrl(token),
            MediaStorageKey.PublicId(assetKey),
            expiresAtUtc);
    }

    /// <summary>
    /// Resolves a caller's reference within its organisation and mints. A cross-org or unknown
    /// reference yields no token (migration plan §7.5).
    /// </summary>
    public async Task<MediaTokenMintResult> MintForReferenceAsync(
        Guid organizationId,
        string imageRefKind,
        Guid imageRefId,
        MediaScope scope,
        CancellationToken cancellationToken = default)
    {
        var asset = await _locator
            .FindByReferenceAsync(organizationId, imageRefKind, imageRefId, cancellationToken)
            .ConfigureAwait(false);

        return asset is null
            ? MediaTokenMintResult.NotFound()
            : Mint(organizationId, asset.AssetKey, scope);
    }

    private string BuildUrl(string token) =>
        $"{_options.PublicBaseUrl.TrimEnd('/')}/api/v1/media/{token}";
}
