namespace Aveline.Api.Modules.Media;

/// <summary>Why a presented media token was refused (migration plan §7.5).</summary>
public enum MediaTokenFailure
{
    /// <summary>The token was accepted.</summary>
    None,

    /// <summary>The token could not be parsed at all.</summary>
    Malformed,

    /// <summary>The MAC did not match the encoded payload.</summary>
    BadSignature,

    /// <summary>The token is past <c>exp</c>, allowing the configured clock skew.</summary>
    Expired,

    /// <summary>The token's scope is not the scope the route requires.</summary>
    ScopeMismatch,

    /// <summary>A single-use nonce was already claimed.</summary>
    Replayed,
}

/// <summary>
/// The result of <see cref="IMediaUrlSigner.Verify"/>. On success the decoded claims are carried
/// so the route can resolve the row without parsing the token a second time.
/// </summary>
/// <param name="IsValid">Whether the token passed every check.</param>
/// <param name="Failure">Why it was refused; <see cref="MediaTokenFailure.None"/> on success.</param>
/// <param name="PublicId">The asset the token names, on success.</param>
/// <param name="OrganizationId">The tenant the token is bound to, on success.</param>
/// <param name="Scope">The scope bound in the token, on success.</param>
public sealed record MediaTokenValidation(
    bool IsValid,
    MediaTokenFailure Failure = MediaTokenFailure.None,
    string? PublicId = null,
    Guid? OrganizationId = null,
    MediaScope? Scope = null)
{
    /// <summary>A refusal carrying only the reason.</summary>
    public static MediaTokenValidation Refused(MediaTokenFailure failure) => new(false, failure);
}
