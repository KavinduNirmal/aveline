using System.Security.Cryptography;
using System.Text;

namespace Aveline.Api.Modules.Organizations.Webhooks;

/// <summary>
/// Verifies Clerk webhook signatures (Svix scheme). Clerk signs
/// <c>{svix-id}.{svix-timestamp}.{body}</c> with HMAC-SHA256 using the base64 secret
/// behind the <c>whsec_</c> prefix and sends the base64 digest in
/// <c>svix-signature</c> as one or more space-separated <c>v1,&lt;digest&gt;</c> values.
/// </summary>
public static class ClerkWebhookVerifier
{
    public const string SecretPrefix = "whsec_";
    public const string SignaturePrefix = "v1,";

    public static bool Verify(
        string? secret,
        string? svixId,
        string? svixTimestamp,
        string? svixSignature,
        byte[] body,
        TimeSpan tolerance,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret)
            || string.IsNullOrWhiteSpace(svixId)
            || string.IsNullOrWhiteSpace(svixTimestamp)
            || string.IsNullOrWhiteSpace(svixSignature)
            || body.Length == 0)
        {
            return false;
        }

        if (!long.TryParse(svixTimestamp, out var unixSeconds))
        {
            return false;
        }

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if ((now - sentAt).Duration() > tolerance)
        {
            // Replay guard: a stale signature is rejected even if the HMAC matches.
            return false;
        }

        var secretValue = secret.StartsWith(SecretPrefix, StringComparison.Ordinal)
            ? secret[SecretPrefix.Length..]
            : secret;

        byte[] key;
        try
        {
            key = Convert.FromBase64String(secretValue);
        }
        catch (FormatException)
        {
            return false;
        }

        var signedContent = $"{svixId}.{svixTimestamp}.{Encoding.UTF8.GetString(body)}";
        var expected = Convert.ToBase64String(
            HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signedContent)));

        foreach (var candidate in svixSignature.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!candidate.StartsWith(SignaturePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var provided = candidate[SignaturePrefix.Length..];
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds a valid signature header for tests and local tooling.</summary>
    public static string Sign(string secret, string svixId, string svixTimestamp, byte[] body)
    {
        var secretValue = secret.StartsWith(SecretPrefix, StringComparison.Ordinal)
            ? secret[SecretPrefix.Length..]
            : secret;
        var signedContent = $"{svixId}.{svixTimestamp}.{Encoding.UTF8.GetString(body)}";
        var digest = Convert.ToBase64String(
            HMACSHA256.HashData(Convert.FromBase64String(secretValue), Encoding.UTF8.GetBytes(signedContent)));
        return $"{SignaturePrefix}{digest}";
    }
}
