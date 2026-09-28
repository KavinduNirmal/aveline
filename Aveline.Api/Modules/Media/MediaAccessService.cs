using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// Resolves a verified token to an asset server-side, and serves it through the provider seam.
/// This is the one place a protected read happens (migration plan §7.3): the route is a streaming
/// proxy because <c>exp</c> can only be re-checked here.
/// </summary>
/// <remarks>
/// <para>
/// <b>The per-row dispatch of strategy §3.3, verbatim:</b> a <c>cloudinary</c> row with a key is
/// read from the provider while <c>Media:ReadFromCloudinary</c> is on; otherwise the row's own
/// bytes are served; otherwise the request is a <c>404</c>. Flipping the flag is the documented
/// rollback and needs no revert (strategy §5.2 stage 2).
/// </para>
/// <para>
/// A provider failure is never silently replaced by the row's copy: a denied asset answers
/// <c>502</c>, not a fallback image (migration plan §7.7).
/// </para>
/// </remarks>
public sealed class MediaAccessService
{
    private readonly IMediaUrlSigner _signer;
    private readonly IMediaAssetLocator _locator;
    private readonly IMediaStorage _storage;
    private readonly MediaOptions _options;
    private readonly ILogger<MediaAccessService> _logger;

    public MediaAccessService(
        IMediaUrlSigner signer,
        IMediaAssetLocator locator,
        IMediaStorage storage,
        IOptions<MediaOptions> options,
        ILogger<MediaAccessService> logger)
    {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Verifies a token against the scopes the route serves, returning the first definitive answer.
    /// </summary>
    /// <remarks>
    /// A route may serve more than one scope. Each attempt is a full
    /// <see cref="IMediaUrlSigner.Verify"/>, which is safe because the fixed order checks the MAC
    /// and <c>exp</c> before the scope, and the single-use nonce only after the scope matches —
    /// so a wrong scope can never consume the nonce, and a nonce is claimed at most once.
    /// </remarks>
    public MediaTokenValidation VerifyFor(string token, IReadOnlyCollection<MediaScope> acceptedScopes)
    {
        ArgumentNullException.ThrowIfNull(acceptedScopes);

        MediaTokenValidation last = MediaTokenValidation.Refused(MediaTokenFailure.ScopeMismatch);
        foreach (var scope in acceptedScopes)
        {
            var validation = _signer.Verify(token, scope);
            if (validation.Failure != MediaTokenFailure.ScopeMismatch)
            {
                return validation;
            }

            last = validation;
        }

        return last;
    }

    /// <summary>Verifies a presented token, resolves the row, and serves the bytes.</summary>
    public async Task<MediaAccessResult> OpenAsync(
        string token,
        IReadOnlyCollection<MediaScope> acceptedScopes,
        CancellationToken cancellationToken = default)
    {
        var validation = VerifyFor(token, acceptedScopes);
        if (!validation.IsValid)
        {
            return MediaAccessResult.Refused(MapTokenFailure(validation.Failure));
        }

        var asset = await _locator
            .FindByAssetKeyAsync(validation.OrganizationId!.Value, validation.PublicId!, cancellationToken)
            .ConfigureAwait(false);

        // 404 before any provider call (migration plan §7.4).
        return asset is null
            ? MediaAccessResult.Refused(MediaAccessFailure.NotFound)
            : await ServeAsync(asset, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The per-row dispatch, exposed so the rollback branch is directly assertable.</summary>
    internal async Task<MediaAccessResult> ServeAsync(
        MediaAccessAsset asset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        // 1. The provider, for a row that names one, while the per-row flag is on.
        if (IsCloudinaryRow(asset) && _options.ReadFromCloudinary)
        {
            try
            {
                var stream = await _storage.OpenReadAsync(
                    new StoredMediaRef(asset.StorageProvider!, asset.StorageKey!, asset.ContentType),
                    cancellationToken).ConfigureAwait(false);

                return stream is null
                    ? MediaAccessResult.Refused(MediaAccessFailure.NotFound)
                    : MediaAccessResult.Served(
                        new MediaAccessGrant(asset.AssetKey, asset.PublicId, asset.ContentType, stream));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (MediaStorageException exception)
            {
                var failure = MapProviderFailure(exception);
                _logger.LogError(
                    "The media provider failed for one protected read ({Failure}): {Detail}",
                    failure,
                    exception.Message);
                return MediaAccessResult.Refused(failure, exception.Message);
            }
        }

        // 2. The database tier's copy — the rollback branch, and the only branch for a row with no
        //    Cloudinary key.
        if (asset.ImageData is { Length: > 0 } bytes)
        {
            return MediaAccessResult.Served(
                new MediaAccessGrant(
                    asset.AssetKey, asset.PublicId, asset.ContentType,
                    new MemoryStream(bytes, writable: false)));
        }

        // 3. Nothing to serve: 404, never 500.
        return MediaAccessResult.Refused(MediaAccessFailure.NotFound);
    }

    private static bool IsCloudinaryRow(MediaAccessAsset asset) =>
        string.Equals(asset.StorageProvider, CloudinaryMediaStorage.ProviderName, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(asset.StorageKey);

    private static MediaAccessFailure MapTokenFailure(MediaTokenFailure failure) => failure switch
    {
        MediaTokenFailure.Malformed => MediaAccessFailure.Malformed,
        MediaTokenFailure.BadSignature => MediaAccessFailure.BadSignature,
        MediaTokenFailure.Expired => MediaAccessFailure.Expired,
        MediaTokenFailure.ScopeMismatch => MediaAccessFailure.ScopeMismatch,
        MediaTokenFailure.Replayed => MediaAccessFailure.Replayed,
        MediaTokenFailure.NonceStoreUnavailable => MediaAccessFailure.NonceStoreUnavailable,
        _ => MediaAccessFailure.Malformed,
    };

    private static MediaAccessFailure MapProviderFailure(MediaStorageException exception) =>
        exception.StatusCode switch
        {
            401 or 403 => MediaAccessFailure.ProviderDenied,
            420 => MediaAccessFailure.ProviderRateLimited,
            408 or 504 => MediaAccessFailure.ProviderTimeout,
            _ when IsTimeout(exception) => MediaAccessFailure.ProviderTimeout,
            _ => MediaAccessFailure.ProviderError,
        };

    private static bool IsTimeout(MediaStorageException exception) =>
        exception.InnerException is TimeoutException or TaskCanceledException or OperationCanceledException;
}
