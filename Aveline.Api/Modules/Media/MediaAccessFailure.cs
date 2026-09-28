namespace Aveline.Api.Modules.Media;

/// <summary>
/// Why a protected media request was refused. The mapping from each value to an HTTP status is
/// fixed by migration plan §7.7; the route is the only place that decides it.
/// </summary>
public enum MediaAccessFailure
{
    /// <summary>The request was served.</summary>
    None,

    /// <summary>The token could not be parsed.</summary>
    Malformed,

    /// <summary>The MAC did not match the encoded payload.</summary>
    BadSignature,

    /// <summary>The token is past <c>exp</c>, allowing the configured clock skew.</summary>
    Expired,

    /// <summary>The token's scope is not one the route serves.</summary>
    ScopeMismatch,

    /// <summary>The single-use nonce was already claimed.</summary>
    Replayed,

    /// <summary>The nonce store is unavailable. Fails closed: no claim, no fetch.</summary>
    NonceStoreUnavailable,

    /// <summary>The row is absent, cross-org, or carries nothing to serve.</summary>
    NotFound,

    /// <summary>The provider denied the fetch (<c>401</c>/<c>403</c>).</summary>
    ProviderDenied,

    /// <summary>The provider's rate limit (<c>420</c>) persisted through the retries.</summary>
    ProviderRateLimited,

    /// <summary>The provider did not answer in time.</summary>
    ProviderTimeout,

    /// <summary>Any other provider failure.</summary>
    ProviderError,
}
