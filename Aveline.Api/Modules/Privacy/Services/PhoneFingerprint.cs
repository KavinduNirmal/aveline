using System.Security.Cryptography;
using System.Text;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// The one-way, deterministic fingerprint of an E.164 phone number used for privacy counters and
/// cache keys (plan §5.3). It exists so no counter key, log line or audit row has to carry a number
/// that identifies a person.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deterministic, not salted.</b> The value is a grouping key, not a credential: it must agree
/// across instances and across the 24-hour acknowledgement window, so a per-process salt would
/// break it. It is never returned to a client and never logged.
/// </para>
/// <para>
/// <b>Not <c>InvitationTokens.Hash</c> reused.</b> That helper is hex and is part of a different
/// contract; this is base64url so the fingerprint can sit inside a cache key or an
/// <c>Idempotency-Key</c> without escaping.
/// </para>
/// </remarks>
public static class PhoneFingerprint
{
    private const string Prefix = "phone:";

    /// <summary>Returns the URL-safe base64 SHA-256 of the canonical number.</summary>
    public static string Of(string phoneE164)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneE164);

        var canonical = $"{Prefix}{phoneE164.Trim()}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
