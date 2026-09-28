namespace Aveline.Api.Modules.Media;

/// <summary>
/// Mints and verifies an Aveline media access token. Interface only in S0: the HMAC
/// implementation (<c>HmacMediaUrlSigner</c>, unit U2.1) is the seam an Advanced-plan upgrade or
/// the <c>private_download_url</c> fallback replaces without touching a route (strategy §3.1).
/// </summary>
public interface IMediaUrlSigner
{
    /// <summary>Mints a token. The token, not the URL, is the grant.</summary>
    string Mint(MediaTokenRequest request, out DateTimeOffset expiresAtUtc);

    /// <summary>Verifies a presented token against the scope the caller requires.</summary>
    MediaTokenValidation Verify(string token, MediaScope requiredScope, string? ipAddress = null);
}
