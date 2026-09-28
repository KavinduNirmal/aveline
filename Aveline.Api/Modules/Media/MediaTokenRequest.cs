namespace Aveline.Api.Modules.Media;

/// <summary>Inputs to <see cref="IMediaUrlSigner.Mint"/> (migration plan §7.4).</summary>
/// <param name="OrganizationId">The tenant the token is bound to; a token cannot cross tenants.</param>
/// <param name="PublicId">The asset the token names.</param>
/// <param name="Scope">What the token may be used for.</param>
/// <param name="Ttl">The requested lifetime; the signer enforces a per-scope hard cap.</param>
/// <param name="SingleUse">Whether the nonce must be claimed exactly once.</param>
/// <param name="IpAddress">Optional IP binding; deliberately unused by default (§7.5).</param>
public sealed record MediaTokenRequest(
    Guid OrganizationId,
    string PublicId,
    MediaScope Scope,
    TimeSpan Ttl,
    bool SingleUse,
    string? IpAddress = null);
