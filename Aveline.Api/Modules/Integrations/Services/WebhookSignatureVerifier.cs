using System.Security.Cryptography;
using System.Text;

namespace Aveline.Api.Modules.Integrations.Services;

/// <summary>
/// Verifies Meta webhook signatures. Meta signs the raw request body with HMAC-SHA256 using
/// the app secret and sends it as <c>X-Hub-Signature-256: sha256=&lt;hex&gt;</c>. Comparison is
/// constant-time to avoid timing side channels.
/// </summary>
public static class WebhookSignatureVerifier
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="signature"/> matches an HMAC-SHA256 of
    /// <paramref name="body"/> keyed by <paramref name="appSecret"/>.
    /// </summary>
    public static bool Verify(string? signature, byte[] body, string appSecret)
    {
        if (string.IsNullOrWhiteSpace(signature) || body.Length == 0 || string.IsNullOrWhiteSpace(appSecret))
        {
            return false;
        }

        const string prefix = "sha256=";
        if (!signature.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var providedHex = signature[prefix.Length..];
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(providedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var actual = hmac.ComputeHash(body);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
