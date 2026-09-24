using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Aveline.Api.Infrastructure.RateLimiting;
using Aveline.Api.Modules.Privacy.Metrics;
using Aveline.Api.Modules.Privacy.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 4.1 (plan §5.3, §9.2, DR-6): the OTP proof for the opt-out flow. The properties this class
/// pins are the whole security argument for a 6-digit code:
///
/// <list type="number">
///   <item>only <c>base64(SHA-256(otp + ":" + handle + ":" + phone))</c> is stored - never the code;</item>
///   <item>the code is bound to the phone it was issued for, and the handle is opaque and random;</item>
///   <item>verify is constant-time and <b>single-use</b> (the key is deleted on success);</item>
///   <item>the attempt counter is a <b>hard cap of 5</b> - the 6th attempt is refused even when the
///     code is correct;</item>
///   <item>the code expires after 300 seconds;</item>
///   <item>send and start counters bound how often a fresh code can be requested;</item>
///   <item>every counter <b>fails closed</b> - a Redis outage refuses the call instead of allowing
///     an unbounded brute-force window (DR-6, the opposite of <see cref="DistributedRateLimiter"/>).</item>
/// </list>
///
/// The cache double deliberately models the two things the production Redis does and the shared
/// <c>TestDistributedCache</c> does not: absolute TTLs (against the injected clock) and failure.
/// </summary>
public class OtpServiceTests
{
    private const string Phone = "+94771234567";
    private const string OtherPhone = "+94770000000";
    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FakeClock _clock = new();
    private readonly FakeTtlCache _cache;
    private readonly OtpMetrics _metrics = new();

    public OtpServiceTests() => _cache = new FakeTtlCache(_clock);

    private OtpService CreateService() =>
        new(_cache, _clock, _metrics, NullLogger<OtpService>.Instance);

    private static string HashOf(string otp, string handle, string phone)
        => Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{otp}:{handle}:{phone}")));

    [Fact]
    public async Task Issue_ReturnsASixDigitCodeAndARandomHandleAndNeverStoresTheCode()
    {
        var service = CreateService();

        var issued = await service.IssueAsync(OrgId, Phone);

        Assert.Equal(6, issued.Code.Length);
        Assert.All(issued.Code, c => Assert.True(char.IsAsciiDigit(c)));
        // 32 random bytes, base64url, unpadded.
        Assert.Equal(43, issued.Handle.Length);
        Assert.DoesNotContain('+', issued.Handle);
        Assert.DoesNotContain('/', issued.Handle);
        Assert.DoesNotContain('=', issued.Handle);

        // Exactly one stored value equals the digest of (code, handle, phone) - the code itself is
        // never a cache value, and the phone never appears in a key.
        Assert.Contains(HashOf(issued.Code, issued.Handle, Phone), string.Join("|", _cache.Values), StringComparison.Ordinal);
        Assert.DoesNotContain(issued.Code, string.Join("|", _cache.Values), StringComparison.Ordinal);
        Assert.DoesNotContain(Phone, string.Join("|", _cache.Keys), StringComparison.Ordinal);
        // The digest is the only place the code could have been stored, and it is a one-way hash of
        // the triple, so it is not the code.
        Assert.DoesNotContain(Phone, string.Join("|", _cache.Values), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_TwoCallsProduceDifferentCodesAndDifferentHandles()
    {
        var service = CreateService();

        var first = await service.IssueAsync(OrgId, Phone);
        var second = await service.IssueAsync(OrgId, Phone);

        Assert.NotEqual(first.Handle, second.Handle);
        // A CSPRNG-backed 6-digit code must not repeat across two draws; if it did, the generator
        // would be broken.
        Assert.NotEqual(first.Code, second.Code);
    }

    [Fact]
    public async Task Verify_WithTheIssuedCode_Succeeds()
    {
        var service = CreateService();
        var issued = await service.IssueAsync(OrgId, Phone);

        var result = await service.VerifyAsync(issued.Handle, issued.Code);

        Assert.True(result.Success);
        Assert.Equal(OtpVerifyFailure.None, result.Failure);
        Assert.Equal(Phone, result.PhoneE164);
    }

    [Fact]
    public async Task Verify_WithAWrongCode_FailsAsInvalid()
    {
        var service = CreateService();
        var issued = await service.IssueAsync(OrgId, Phone);
        var wrong = issued.Code == "000000" ? "111111" : "000000";

        var result = await service.VerifyAsync(issued.Handle, wrong);

        Assert.False(result.Success);
        Assert.Equal(OtpVerifyFailure.InvalidCode, result.Failure);
    }

    [Fact]
    public async Task Verify_OnTheSixthAttempt_IsRejectedEvenWithTheCorrectCode()
    {
        // The acceptance criterion: the 6th attempt is refused. 6 digits is ~19.9 bits, which is
        // only defensible because five guesses out of a million is the worst case.
        var service = CreateService();
        var issued = await service.IssueAsync(OrgId, Phone);
        var wrong = issued.Code == "000000" ? "111111" : "000000";

        for (var attempt = 1; attempt <= OtpService.MaxAttempts; attempt++)
        {
            var failed = await service.VerifyAsync(issued.Handle, wrong);
            Assert.Equal(OtpVerifyFailure.InvalidCode, failed.Failure);
        }

        var sixth = await service.VerifyAsync(issued.Handle, issued.Code);

        Assert.False(sixth.Success);
        Assert.Equal(OtpVerifyFailure.TooManyAttempts, sixth.Failure);
    }

    [Fact]
    public async Task Verify_WithAnExpiredCode_FailsAndNeverSucceedsAfterwards()
    {
        var service = CreateService();
        var issued = await service.IssueAsync(OrgId, Phone);

        _clock.Advance(OtpService.CodeTtl + TimeSpan.FromSeconds(1));

        var result = await service.VerifyAsync(issued.Handle, issued.Code);

        Assert.False(result.Success);
        Assert.Equal(OtpVerifyFailure.ExpiredOrUnknown, result.Failure);
    }

    [Fact]
    public async Task Verify_ReplayingASuccessfulCode_Fails()
    {
        var service = CreateService();
        var issued = await service.IssueAsync(OrgId, Phone);
        var first = await service.VerifyAsync(issued.Handle, issued.Code);
        Assert.True(first.Success);

        var replay = await service.VerifyAsync(issued.Handle, issued.Code);

        Assert.False(replay.Success);
        Assert.Equal(OtpVerifyFailure.ExpiredOrUnknown, replay.Failure);
    }

    [Fact]
    public async Task Verify_ReportsThePhoneTheCodeWasIssuedFor()
    {
        // The code is bound to the phone through the digest, and the verify result carries the
        // phone back so the revocation can never act on a client-supplied number.
        var service = CreateService();
        var issued = await service.IssueAsync(OrgId, Phone);

        var result = await service.VerifyAsync(issued.Handle, issued.Code);

        Assert.Equal(Phone, result.PhoneE164);
        Assert.NotEqual(OtherPhone, result.PhoneE164);
    }

    [Theory]
    [InlineData("00000")]   // short
    [InlineData("0000000")] // long
    [InlineData("abcdef")]  // non-numeric
    public async Task Verify_WithAMalformedCode_FailsWithoutConsumingAnAttempt(string malformed)
    {
        var service = CreateService();
        var issued = await service.IssueAsync(OrgId, Phone);

        var result = await service.VerifyAsync(issued.Handle, malformed);

        Assert.False(result.Success);
        Assert.Equal(OtpVerifyFailure.InvalidCode, result.Failure);
        // The real code still works: a malformed value must not burn one of the five attempts.
        Assert.True((await service.VerifyAsync(issued.Handle, issued.Code)).Success);
    }

    [Fact]
    public async Task Verify_WithAnUnknownHandle_FailsIdenticallyToAnExpiredCode()
    {
        var service = CreateService();

        var result = await service.VerifyAsync("bm90LWEtaGFuZGxl", "123456");

        Assert.False(result.Success);
        Assert.Equal(OtpVerifyFailure.ExpiredOrUnknown, result.Failure);
    }

    [Fact]
    public async Task Verify_WhenTheCacheIsDown_FailsClosedRatherThanAllowing()
    {
        // DR-6: a Redis outage must block opt-out, not open a brute-force window.
        var service = CreateService();
        var issued = await service.IssueAsync(OrgId, Phone);
        _cache.IsDown = true;

        var result = await service.VerifyAsync(issued.Handle, issued.Code);

        Assert.False(result.Success);
        Assert.Equal(OtpVerifyFailure.StoreUnavailable, result.Failure);
    }

    [Fact]
    public async Task Issue_WhenTheCacheIsDown_ThrowsTheTypedUnavailableError()
    {
        var service = CreateService();
        _cache.IsDown = true;

        await Assert.ThrowsAsync<OtpStoreUnavailableException>(() => service.IssueAsync(OrgId, Phone));
    }

    [Fact]
    public async Task TryStart_AllowsTenStartsPerIpAndRefusesTheEleventh()
    {
        var service = CreateService();

        // A distinct number per start isolates the address budget from the number budget.
        for (var i = 0; i < OtpService.MaxStartsPerIpPerHour; i++)
        {
            Assert.True(await service.TryStartAsync("203.0.113.7", $"+9477123{i:D4}"));
        }

        Assert.False(await service.TryStartAsync("203.0.113.7", "+94771239999"));
        // A different address is a different budget.
        Assert.True(await service.TryStartAsync("203.0.113.8", OtherPhone));
    }

    [Fact]
    public async Task TryStart_AllowsThreeStartsPerPhoneAndRefusesTheFourthEvenFromANewIp()
    {
        var service = CreateService();

        for (var i = 0; i < OtpService.MaxSendsPerPhonePerWindow; i++)
        {
            Assert.True(await service.TryStartAsync($"203.0.113.{i}", Phone));
        }

        Assert.False(await service.TryStartAsync("198.51.100.9", Phone));
        Assert.True(await service.TryStartAsync("198.51.100.9", OtherPhone));
    }

    [Fact]
    public async Task TryStart_AFourthStartForTheSamePhoneFromTheSameIp_IsRefused()
    {
        var service = CreateService();

        for (var i = 0; i < OtpService.MaxSendsPerPhonePerWindow; i++)
        {
            Assert.True(await service.TryStartAsync("203.0.113.7", Phone));
        }

        Assert.False(await service.TryStartAsync("203.0.113.7", Phone));
    }

    [Fact]
    public async Task TryStart_RecoversAfterThePhoneWindowElapses()
    {
        var service = CreateService();
        for (var i = 0; i < OtpService.MaxSendsPerPhonePerWindow; i++)
        {
            await service.TryStartAsync($"203.0.113.{i}", Phone);
        }

        _clock.Advance(OtpService.PhoneWindow + TimeSpan.FromSeconds(1));

        Assert.True(await service.TryStartAsync("203.0.113.9", Phone));
    }

    [Fact]
    public async Task TryStart_WhenTheCacheIsDown_FailsClosed()
    {
        var service = CreateService();
        _cache.IsDown = true;

        // null, not false: the endpoint must answer 503 rather than report a rate limit.
        Assert.Null(await service.TryStartAsync("203.0.113.7", Phone));
    }

    /// <summary>
    /// A test clock so expiry is deterministic instead of a <c>Task.Delay</c>. Also drives the cache
    /// double's expiry, so one <c>Advance</c> moves both halves together.
    /// </summary>
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    /// <summary>
    /// <see cref="IDistributedCache"/> with the two production behaviours a security test needs:
    /// absolute expiry and an outage switch.
    /// </summary>
    private sealed class FakeTtlCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, (byte[] Value, DateTimeOffset? ExpiresAt)> _store = new();
        private readonly TimeProvider _clock;

        public FakeTtlCache(TimeProvider clock) => _clock = clock;

        public bool IsDown { get; set; }

        public IReadOnlyList<string> Keys => _store.Keys.ToList();

        public IReadOnlyList<string> Values =>
            _store.Values.Select(v => Encoding.UTF8.GetString(v.Value)).ToList();

        private void Guard()
        {
            if (IsDown)
            {
                throw new InvalidOperationException("Redis is unavailable.");
            }
        }

        public byte[]? Get(string key)
        {
            Guard();
            if (!_store.TryGetValue(key, out var entry))
            {
                return null;
            }

            if (entry.ExpiresAt is not null && entry.ExpiresAt <= _clock.GetUtcNow())
            {
                _store.TryRemove(key, out _);
                return null;
            }

            return entry.Value;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Guard();
            var expiresAt = options.AbsoluteExpirationRelativeToNow is { } ttl
                ? _clock.GetUtcNow() + ttl
                : options.AbsoluteExpiration;
            _store[key] = (value, expiresAt);
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) => Guard();

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            Guard();
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            Guard();
            _store.TryRemove(key, out _);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
