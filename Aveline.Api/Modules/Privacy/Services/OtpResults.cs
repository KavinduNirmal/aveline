namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// Why a verify call did not succeed. The vocabulary exists so the endpoint can answer the same
/// <c>400 otp-invalid</c> for every one of them (anti-enumeration) while the log and the audit
/// distinguish a brute-force attempt from an expired code.
/// </summary>
public enum OtpVerifyFailure
{
    /// <summary>The code was correct; the revocation may proceed.</summary>
    None,

    /// <summary>The handle is unknown, the code expired, or the code was already used.</summary>
    ExpiredOrUnknown,

    /// <summary>The code did not match.</summary>
    InvalidCode,

    /// <summary>The five-attempt cap is spent; the code is no longer verifiable.</summary>
    TooManyAttempts,

    /// <summary>The store could not be read. The call is refused, never allowed.</summary>
    StoreUnavailable,
}

/// <summary>One verify outcome. <see cref="PhoneE164"/> is set only on success.</summary>
public sealed record OtpVerifyResult(
    bool Success,
    OtpVerifyFailure Failure,
    string? PhoneE164 = null)
{
    /// <summary>The code was correct; <paramref name="phoneE164"/> is the number it proved.</summary>
    public static OtpVerifyResult Verified(string phoneE164) =>
        new(true, OtpVerifyFailure.None, phoneE164);

    /// <summary>The call is refused.</summary>
    public static OtpVerifyResult Rejected(OtpVerifyFailure failure) => new(false, failure);
}

/// <summary>One issued code: the plaintext (returned once, never stored) and the opaque handle.</summary>
/// <param name="Code">The six digits to send.</param>
/// <param name="Handle">The opaque 32-byte base64url reference the client returns on verify.</param>
/// <param name="ExpiresAtUtc">When the code stops verifying.</param>
public sealed record OtpIssueResult(string Code, string Handle, DateTimeOffset ExpiresAtUtc);
