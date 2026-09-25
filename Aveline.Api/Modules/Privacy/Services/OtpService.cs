using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aveline.Api.Modules.Privacy.Metrics;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.Extensions.Caching.Distributed;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// Issues and verifies the six-digit opt-out OTP (plan §5.2, §5.3). It is the only component that
/// knows the code, and it never persists it in the clear.
/// </summary>
/// <remarks>
/// <para>
/// <b>The code is bound to a phone and never stored plaintext.</b> The stored digest is
/// <c>base64(SHA-256(code + ":" + handle + ":" + phone))</c>; the handle is 32 random bytes of
/// base64url so it can be handed to an anonymous client without disclosing which numbers have a
/// pending code; and the phone is part of the hashed input so a code minted for one number is not a
/// proof for another. The phone is also carried (base64, so no cache key, log line or dump shows a
/// number) alongside the digest, because verify must report which number it proved -
/// <see cref="VerifyAsync"/> returns it so no caller has to trust the number in the request body.
/// </para>
/// <para>
/// <b>OTP entropy.</b> Six decimal digits is about 19.9 bits, which on its own is far too little for
/// a bearer proof. It is defensible only in combination with the three caps: the hard five-attempt
/// limit bounds a single code's success probability at <c>5/10^6</c>; the 300-second TTL bounds how
/// long an attacker has to spend those five guesses; and the three-sends-per-number-per-15-minutes
/// counter bounds how fast a fresh code can be requested once a code is burned. Remove any one of
/// the three and the entropy argument fails.
/// </para>
/// <para>
/// <b>Fail closed.</b> Every counter is read straight from <see cref="IDistributedCache"/> rather
/// than through <c>IRateLimiter</c>, because the shared limiter deliberately fails open
/// (<c>DistributedRateLimiter.cs</c>) and an opt-out control must not (DR-6). A cache error during
/// issue throws <see cref="OtpStoreUnavailableException"/>; during verify it is
/// <see cref="OtpVerifyFailure.StoreUnavailable"/>. Both become <c>503</c> at the endpoint.
/// </para>
/// </remarks>
public sealed class OtpService : IOtpService
{
    // The six digits. RandomNumberGenerator is the CSPRNG: Random.Shared would make a code
    // predictable from previous codes, which turns the five-attempt cap into a formality.
    private const int CodeDigits = 6;
    private const int HandleBytes = 32;

    /// <summary>The hard cap. The sixth verify call is refused even with the correct code.</summary>
    public const int MaxAttempts = 5;

    /// <summary>How long a code verifies.</summary>
    public static readonly TimeSpan CodeTtl = TimeSpan.FromSeconds(300);

    /// <summary>How long the attempt counter outlives the code.</summary>
    public static readonly TimeSpan AttemptWindow = TimeSpan.FromSeconds(300);

    /// <summary>Fresh codes a single number may request inside <see cref="PhoneWindow"/>.</summary>
    public const int MaxSendsPerPhonePerWindow = 3;

    /// <summary>The phone send window: 15 minutes.</summary>
    public static readonly TimeSpan PhoneWindow = TimeSpan.FromMinutes(15);

    /// <summary>Starts a single address may make inside <see cref="IpWindow"/>.</summary>
    public const int MaxStartsPerIpPerHour = 10;

    /// <summary>The IP start window: one hour.</summary>
    public static readonly TimeSpan IpWindow = TimeSpan.FromHours(1);

    // UnsafeRelaxedJsonEscaping keeps the base64 digest byte-for-byte in the cache value; the
    // default encoder would write `+` as `\u002B`, which makes an incident-time `GET otp:code:*`
    // unreadable for no benefit. The value is internal, never HTML, and never echoed to a client.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IDistributedCache _cache;
    private readonly TimeProvider _clock;
    private readonly OtpMetrics _metrics;
    private readonly ILogger<OtpService> _logger;

    public OtpService(
        IDistributedCache cache,
        TimeProvider clock,
        OtpMetrics metrics,
        ILogger<OtpService> logger)
    {
        _cache = cache;
        _clock = clock;
        _metrics = metrics;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OtpIssueResult> IssueAsync(
        Guid organizationId,
        string phoneE164,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneE164);

        var handle = CreateHandle();
        var code = CreateCode();
        var expiresAt = _clock.GetUtcNow() + CodeTtl;

        var state = new StoredCode(Digested(code, handle, phoneE164), Encode(phoneE164));

        try
        {
            await _cache.SetStringAsync(
                CodeKey(handle),
                JsonSerializer.Serialize(state, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CodeTtl },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A code that was not stored can never verify, so returning it would hand the customer
            // a dead end. Refuse instead (DR-6).
            _logger.LogError(ex, "OTP storage unavailable for organization {OrganizationId}.", organizationId);
            throw new OtpStoreUnavailableException(ex);
        }

        // No log line carries the code or the number. The handle and the organization are enough to
        // correlate with the endpoint's audit row.
        _logger.LogInformation(
            "OTP issued for organization {OrganizationId} handle {Handle}.", organizationId, handle);

        return new OtpIssueResult(code, handle, expiresAt);
    }

    /// <inheritdoc />
    public async Task<OtpVerifyResult> VerifyAsync(
        string? handle,
        string? code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return OtpVerifyResult.Rejected(OtpVerifyFailure.ExpiredOrUnknown);
        }

        StoredCode? stored;
        int attempts;
        try
        {
            var raw = await _cache.GetStringAsync(CodeKey(handle), cancellationToken);

            // The attempt counter is consumed before the digest is read: an unknown handle must
            // still cost a guess so the counter cannot be probed for free.
            attempts = await IncrementAsync(AttemptsKey(handle), AttemptWindow, cancellationToken);
            stored = Deserialize(raw);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "OTP verify could not read the store; refusing (fail closed).");
            return OtpVerifyResult.Rejected(OtpVerifyFailure.StoreUnavailable);
        }

        // Order matters: the cap is checked before the digest so the sixth call is refused even
        // when it carries the right code.
        if (attempts > MaxAttempts)
        {
            return OtpVerifyResult.Rejected(OtpVerifyFailure.TooManyAttempts);
        }

        if (stored is null)
        {
            // Unknown handle, expired, or already-used code. The three are deliberately one answer.
            return OtpVerifyResult.Rejected(OtpVerifyFailure.ExpiredOrUnknown);
        }

        if (!IsWellFormed(code))
        {
            // A malformed value must not count as a guess, but it must not be compared either.
            return OtpVerifyResult.Rejected(OtpVerifyFailure.InvalidCode);
        }

        var phone = Decode(stored.PhoneB64);
        if (phone is null)
        {
            return OtpVerifyResult.Rejected(OtpVerifyFailure.ExpiredOrUnknown);
        }

        // Constant-time compare, in the shape the webhook signature check uses
        // (WebhookEndpoints.cs). A length mismatch answers false without leaking which half failed.
        var supplied = Digested(code!, handle, phone);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(stored.Digest), Encoding.UTF8.GetBytes(supplied)))
        {
            return OtpVerifyResult.Rejected(OtpVerifyFailure.InvalidCode);
        }

        // Single use: the key is gone before the caller sees success, so a replay races a deletion
        // that has already happened.
        try
        {
            await _cache.RemoveAsync(CodeKey(handle), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The code verified, but failing to consume it would allow a replay. Refuse rather than
            // report a success that can be reused.
            _logger.LogError(ex, "OTP verified but could not be consumed; refusing (fail closed).");
            return OtpVerifyResult.Rejected(OtpVerifyFailure.StoreUnavailable);
        }

        return OtpVerifyResult.Verified(phone);
    }

    /// <inheritdoc />
    public async Task<bool?> TryStartAsync(
        string ipAddress,
        string phoneE164,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneE164);

        var ipKey = $"otp:ip:{IpFingerprint(ipAddress)}";
        var phoneKey = $"otp:send:{PhoneFingerprint.Of(phoneE164)}";

        try
        {
            // Both budgets are consumed before either verdict is returned, so a caller who fails
            // the phone budget does not get a free retry on the IP budget (or the reverse). The
            // phone verdict is reported first because it is the tighter, more specific budget: a
            // shared NAT must not turn "this number asked four times" into "this address is banned".
            var ipCount = await IncrementAsync(ipKey, IpWindow, cancellationToken);
            var phoneCount = await IncrementAsync(phoneKey, PhoneWindow, cancellationToken);

            if (phoneCount > MaxSendsPerPhonePerWindow)
            {
                _metrics.RecordRateLimited(PrivacyRateLimitReasons.PhoneBudget);
                return false;
            }

            if (ipCount > MaxStartsPerIpPerHour)
            {
                _metrics.RecordRateLimited(PrivacyRateLimitReasons.IpBudget);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.RecordRateLimited(PrivacyRateLimitReasons.StoreUnavailable);
            _logger.LogError(ex, "OTP start counter unavailable; refusing (fail closed).");
            // null, not false: the caller answers 503 rather than 429 so an outage is not
            // misreported as a rate limit.
            return null;
        }
    }

    /// <summary>
    /// Reads-increments-writes one counter and returns the post-increment value. The TTL is reset on
    /// every write, so a window is "at most N per <paramref name="window"/> from the first attempt",
    /// matching the sliding-window behaviour <c>DistributedRateLimiter</c> documents. Redis has no
    /// atomic increment behind <see cref="IDistributedCache"/>, so a burst can under-count by a few
    /// - acceptable here because the hard caps that protect the code (five attempts, 300 seconds)
    /// do not depend on this counter being exact.
    /// </summary>
    private async Task<int> IncrementAsync(
        string key, TimeSpan window, CancellationToken cancellationToken)
    {
        var raw = await _cache.GetStringAsync(key, cancellationToken);
        var count = int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : 0;
        count++;

        await _cache.SetStringAsync(
            key,
            count.ToString(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = window },
            cancellationToken);

        return count;
    }

    private static StoredCode? Deserialize(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<StoredCode>(raw, Json);
            return state is { Digest.Length: > 0, PhoneB64.Length: > 0 } ? state : null;
        }
        catch (JsonException)
        {
            // A value another writer put under this key is not a code; answer "unknown".
            return null;
        }
    }

    /// <summary>The stored digest for a (code, handle, phone) triple. Never the code alone.</summary>
    internal static string Digested(string code, string handle, string phoneE164)
        => Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{code}:{handle}:{phoneE164}")));

    private static string Encode(string phoneE164)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(phoneE164));

    private static string? Decode(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsWellFormed(string? code)
        => code is { Length: CodeDigits } && code.All(char.IsAsciiDigit);

    private static string CreateCode()
        => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string CreateHandle()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(HandleBytes))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string CodeKey(string handle) => $"otp:code:{handle}";

    private static string AttemptsKey(string handle) => $"otp:attempts:{handle}";

    /// <summary>The IP counter key. Never the raw address, matching <c>AuditLogEntry.IpHash</c>.</summary>
    internal static string IpFingerprint(string? ipAddress)
        => string.IsNullOrWhiteSpace(ipAddress)
            ? "unknown"
            : MetricDimensionHasher.Hash(ipAddress)[..32];

    /// <summary>The cache value: the digest, plus the phone it was bound to (base64, never plain).</summary>
    private sealed record StoredCode(string Digest, string PhoneB64);
}
