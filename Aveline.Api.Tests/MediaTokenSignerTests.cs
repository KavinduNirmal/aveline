using Aveline.Api.Modules.Media;
using FluentAssertions;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U2.1 (lane L1) — <see cref="HmacMediaUrlSigner"/>, the frozen token format
/// (migration plan §7.4–§7.5). Every case below is a verification, not a convenience: the MAC
/// covers the <b>entire encoded payload</b>, the verification order is MAC → <c>exp</c> → scope →
/// nonce, and the TTL hard caps are enforced by the signer rather than merely configured.
/// </summary>
/// <remarks>
/// The clock and the nonce source are injected, so no case depends on the wall clock, a random
/// value, or Redis. The one frozen test-vector triple lives in
/// <see cref="MediaTokenVectorTests"/>; this file asserts the behaviours around it.
/// </remarks>
public class MediaTokenSignerTests
{
    private const string AssetA = "image/authenticated:aveline/11111111-1111-1111-1111-111111111111/conversations/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string AssetB = "image/authenticated:aveline/11111111-1111-1111-1111-111111111111/conversations/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    private static readonly Guid Org = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>32 bytes, base64 — the documented shape of <c>Media:SigningKey</c>.</summary>
    private static readonly string KeyOne =
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());

    private static readonly string KeyTwo =
        Convert.ToBase64String(Enumerable.Range(100, 32).Select(value => (byte)value).ToArray());

    private static readonly DateTimeOffset BaseTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    // ---------------------------------------------------------------------------------------
    // The round trip, and what the claims carry
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void MintThenVerify_RoundTripsTheFrozenClaims()
    {
        var store = new RecordingNonceStore();
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), store);

        var token = signer.Mint(Request(AssetA, MediaScope.VisionAnalyze, TimeSpan.FromSeconds(600)), out var expiresAtUtc);

        var validation = signer.Verify(token, MediaScope.VisionAnalyze);

        validation.IsValid.Should().BeTrue();
        validation.Failure.Should().Be(MediaTokenFailure.None);
        validation.PublicId.Should().Be(AssetA);
        validation.OrganizationId.Should().Be(Org);
        validation.Scope.Should().Be(MediaScope.VisionAnalyze);
        expiresAtUtc.Should().Be(BaseTime.AddSeconds(600));

        // Two base64url segments separated by one dot — the frozen wire shape.
        var parts = token.Split('.');
        parts.Should().HaveCount(2);
        parts.Should().OnlyContain(part => part.Length > 0);
        parts[0].Should().NotContainAny("=", "+", "/");
        parts[1].Should().NotContainAny("=", "+", "/");
    }

    [Fact]
    public void Mint_ForThePublicScope_IsRefused()
    {
        // `asset.public` is not tokenised: catalog imagery is public (F-7/Q4) and gets the CDN
        // URL directly (migration plan §7.5).
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), new RecordingNonceStore());

        var mint = () => signer.Mint(
            Request(AssetA, MediaScope.AssetPublic, TimeSpan.FromSeconds(600)), out _);

        mint.Should().Throw<InvalidOperationException>();
    }

    // ---------------------------------------------------------------------------------------
    // 2. Tamper — a flipped character in the MAC, and in the payload
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Verify_WithAFlippedCharacterInTheMac_IsBadSignature()
    {
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), new RecordingNonceStore());
        var token = Mint(signer, AssetA, MediaScope.VisionAnalyze);
        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{Flip(parts[1], 0)}";

        var validation = signer.Verify(tampered, MediaScope.VisionAnalyze);

        validation.IsValid.Should().BeFalse();
        validation.Failure.Should().Be(MediaTokenFailure.BadSignature);
    }

    [Fact]
    public void Verify_WithAFlippedCharacterInThePayload_IsBadSignature()
    {
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), new RecordingNonceStore());
        var token = Mint(signer, AssetA, MediaScope.VisionAnalyze);
        var parts = token.Split('.');
        var tampered = $"{Flip(parts[0], parts[0].Length - 1)}.{parts[1]}";

        var validation = signer.Verify(tampered, MediaScope.VisionAnalyze);

        validation.IsValid.Should().BeFalse();
        validation.Failure.Should().Be(MediaTokenFailure.BadSignature);
    }

    // ---------------------------------------------------------------------------------------
    // 3. The payload splice — the case that justifies MAC-ing the whole payload
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Verify_WhenTheMacIsValidForOneAssetButThePayloadNamesAnother_IsBadSignature()
    {
        var store = new RecordingNonceStore();
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), store);

        var tokenForA = Mint(signer, AssetA, MediaScope.VisionAnalyze);
        var tokenForB = Mint(signer, AssetB, MediaScope.VisionAnalyze);

        // A's MAC over B's payload: the splice a sign-the-id scheme would accept, because the
        // identifier would be unchanged and only the surrounding claims (asset, exp, scope)
        // would move. The MAC covers the encoded payload, so this is refused (migration §7.4).
        var spliced = $"{tokenForB.Split('.')[0]}.{tokenForA.Split('.')[1]}";

        var validation = signer.Verify(spliced, MediaScope.VisionAnalyze);

        validation.IsValid.Should().BeFalse();
        validation.Failure.Should().Be(MediaTokenFailure.BadSignature);
    }

    [Fact]
    public void Verify_TheSplice_DoesNotConsumeASingleUseNonce()
    {
        // MAC first: a refused splice must not reach the nonce claim, or an attacker could burn a
        // legitimate token's nonce with a garbage presentation.
        var store = new RecordingNonceStore();
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), store);
        var tokenForA = Mint(signer, AssetA, MediaScope.VisionAnalyze);
        var tokenForB = Mint(signer, AssetB, MediaScope.VisionAnalyze);

        signer.Verify($"{tokenForB.Split('.')[0]}.{tokenForA.Split('.')[1]}", MediaScope.VisionAnalyze);

        store.Claims.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------
    // 4. The expiry boundary, including the clock-skew tolerance
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(-1, true)]   // a second inside `exp`
    [InlineData(0, true)]    // exactly at `exp`
    [InlineData(29, true)]   // inside the 30 s skew
    [InlineData(30, true)]   // exactly at the skew edge
    [InlineData(31, false)]  // one second past the skew edge
    public void Verify_AtTheExpiryBoundary_HonoursTheClockSkew(int offsetSeconds, bool expectedValid)
    {
        var clock = new MutableClock(BaseTime);
        var signer = CreateSigner(KeyOne, clock, new RecordingNonceStore());
        var token = Mint(signer, AssetA, MediaScope.AttachmentView);
        var exp = BaseTime.AddSeconds(900);

        clock.Now = exp.AddSeconds(offsetSeconds);
        var validation = signer.Verify(token, MediaScope.AttachmentView);

        validation.IsValid.Should().Be(expectedValid);
        if (!expectedValid)
        {
            validation.Failure.Should().Be(MediaTokenFailure.Expired);
        }
    }

    // ---------------------------------------------------------------------------------------
    // 5. Scope escalation, both directions
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Verify_AnAttachmentTokenWhereVisionIsRequired_IsScopeMismatch()
    {
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), new RecordingNonceStore());
        var token = Mint(signer, AssetA, MediaScope.AttachmentView);

        var validation = signer.Verify(token, MediaScope.VisionAnalyze);

        validation.IsValid.Should().BeFalse();
        validation.Failure.Should().Be(MediaTokenFailure.ScopeMismatch);
    }

    [Fact]
    public void Verify_AVisionTokenWhereAttachmentViewIsRequired_IsScopeMismatch()
    {
        // Scope is checked before the nonce in the fixed order, so a wrong-scope presentation must
        // not consume the single-use claim (migration plan §7.4).
        var store = new RecordingNonceStore();
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), store);
        var token = Mint(signer, AssetA, MediaScope.VisionAnalyze);

        var validation = signer.Verify(token, MediaScope.AttachmentView);

        validation.IsValid.Should().BeFalse();
        validation.Failure.Should().Be(MediaTokenFailure.ScopeMismatch);
        store.Claims.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------
    // 7. The TTL hard cap — enforced by the signer, not merely configured
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Mint_WithAnOverCapVisionTtl_ClampsToTheHardCap()
    {
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), new RecordingNonceStore());

        signer.Mint(Request(AssetA, MediaScope.VisionAnalyze, TimeSpan.FromDays(30)), out var expiresAtUtc);

        // 1800 s, the documented hard cap (migration plan §7.5). A caller cannot mint a 30-day
        // token by asking for one.
        expiresAtUtc.Should().Be(BaseTime.AddSeconds(1800));
    }

    [Fact]
    public void Mint_WithAnOverCapAttachmentTtl_ClampsToTheHardCap()
    {
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), new RecordingNonceStore());

        signer.Mint(Request(AssetA, MediaScope.AttachmentView, TimeSpan.FromDays(30)), out var expiresAtUtc);

        expiresAtUtc.Should().Be(BaseTime.AddSeconds(3600));
    }

    [Fact]
    public void Mint_WhenTheConfiguredDefaultIsOverTheCap_StillClampsToTheHardCap()
    {
        // The cap binds the configuration too: a 30-day Media:VisionTokenTtlSeconds cannot become
        // a 30-day token.
        var options = Options(KeyOne, visionTtlSeconds: (int)TimeSpan.FromDays(30).TotalSeconds);
        var signer = CreateSigner(options, new MutableClock(BaseTime), new RecordingNonceStore());

        signer.Mint(Request(AssetA, MediaScope.VisionAnalyze, TimeSpan.Zero), out var expiresAtUtc);

        expiresAtUtc.Should().Be(BaseTime.AddSeconds(1800));
    }

    [Fact]
    public void Mint_WithAnUnderCapTtl_HonoursTheRequestedLifetime()
    {
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), new RecordingNonceStore());

        signer.Mint(Request(AssetA, MediaScope.VisionAnalyze, TimeSpan.FromSeconds(120)), out var expiresAtUtc);

        expiresAtUtc.Should().Be(BaseTime.AddSeconds(120));
    }

    // ---------------------------------------------------------------------------------------
    // 8. Single-use replay
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Verify_AVisionToken_IsSingleUse()
    {
        var store = new RecordingNonceStore();
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), store);
        var token = Mint(signer, AssetA, MediaScope.VisionAnalyze);

        signer.Verify(token, MediaScope.VisionAnalyze).IsValid.Should().BeTrue();

        var replay = signer.Verify(token, MediaScope.VisionAnalyze);
        replay.IsValid.Should().BeFalse();
        replay.Failure.Should().Be(MediaTokenFailure.Replayed);
        store.Claims.Should().ContainSingle();
    }

    [Fact]
    public void Verify_AnAttachmentToken_IsNotSingleUse()
    {
        // A chat thumbnail and its full-screen view are separate fetches in one session, so a
        // nonce here would break the UI (migration plan §7.5).
        var store = new RecordingNonceStore();
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), store);
        var token = Mint(signer, AssetA, MediaScope.AttachmentView);

        signer.Verify(token, MediaScope.AttachmentView).IsValid.Should().BeTrue();
        signer.Verify(token, MediaScope.AttachmentView).IsValid.Should().BeTrue();
        store.Claims.Should().BeEmpty();
    }

    [Fact]
    public void Verify_WhenTheNonceStoreIsUnavailable_FailsClosed()
    {
        // §3.8/§7.7: for an access grant the direction must be the opposite of the agent's
        // rate limiter — no claim, no fetch.
        var store = new RecordingNonceStore { Unavailable = true };
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), store);
        var token = Mint(signer, AssetA, MediaScope.VisionAnalyze);

        var validation = signer.Verify(token, MediaScope.VisionAnalyze);

        validation.IsValid.Should().BeFalse();
        validation.Failure.Should().Be(MediaTokenFailure.NonceStoreUnavailable);
    }

    // ---------------------------------------------------------------------------------------
    // 9. Key rotation
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Verify_WithATokenMintedUnderAPreviousKey_IsBadSignatureWithNoDetail()
    {
        var oldSigner = CreateSigner(KeyOne, new MutableClock(BaseTime), new RecordingNonceStore());
        var newSigner = CreateSigner(KeyTwo, new MutableClock(BaseTime), new RecordingNonceStore());
        var token = Mint(oldSigner, AssetA, MediaScope.VisionAnalyze);

        var validation = newSigner.Verify(token, MediaScope.VisionAnalyze);

        validation.IsValid.Should().BeFalse();
        validation.Failure.Should().Be(MediaTokenFailure.BadSignature);
        validation.PublicId.Should().BeNull();
        validation.OrganizationId.Should().BeNull();
        validation.Scope.Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------
    // Malformed tokens — never a provider call, never a detail
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("no-dot")]
    [InlineData("a.b.c")]
    [InlineData("..")]
    [InlineData("!!!.???")]
    [InlineData(".AAAA")]
    [InlineData("AAAA.")]
    public void Verify_WithAMalformedToken_IsMalformed(string token)
    {
        var signer = CreateSigner(KeyOne, new MutableClock(BaseTime), new RecordingNonceStore());

        var validation = signer.Verify(token, MediaScope.VisionAnalyze);

        validation.IsValid.Should().BeFalse();
        validation.Failure.Should().BeOneOf(MediaTokenFailure.Malformed, MediaTokenFailure.BadSignature);
    }

    // ---------------------------------------------------------------------------------------
    // The rendered URL: absolute, https, and inside the provider's external limit
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Mint_ThenTheRenderedUrl_IsAbsoluteHttpsAndUnderTheExternalLimit()
    {
        // The provider documents an 8192-character external-URL maximum (strategy §3.5). The
        // payload grows with every claim added, so the length is asserted rather than trusted.
        var options = Options(KeyOne);
        var signer = CreateSigner(options, new MutableClock(BaseTime), new RecordingNonceStore());

        // The longest realistic asset key: the encoded Cloudinary key plus a full GUID pair.
        var token = Mint(signer, AssetA, MediaScope.VisionAnalyze);
        var url = $"{options.PublicBaseUrl}/api/v1/media/{token}";

        Uri.TryCreate(url, UriKind.Absolute, out var uri).Should().BeTrue();
        uri!.Scheme.Should().Be(Uri.UriSchemeHttps);
        url.Length.Should().BeLessThan(8192);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static MediaOptions Options(
        string signingKey,
        int visionTtlSeconds = 600,
        int attachmentTtlSeconds = 900,
        int skewSeconds = 30) => new()
    {
        SigningKey = signingKey,
        PublicBaseUrl = "https://api.aveline.lk",
        VisionTokenTtlSeconds = visionTtlSeconds,
        AttachmentTokenTtlSeconds = attachmentTtlSeconds,
        ClockSkewToleranceSeconds = skewSeconds,
    };

    private static HmacMediaUrlSigner CreateSigner(
        string signingKey,
        TimeProvider clock,
        IMediaTokenNonceStore nonceStore,
        Func<string>? nonceFactory = null)
        => CreateSigner(Options(signingKey), clock, nonceStore, nonceFactory);

    private static HmacMediaUrlSigner CreateSigner(
        MediaOptions options,
        TimeProvider clock,
        IMediaTokenNonceStore nonceStore,
        Func<string>? nonceFactory = null)
        => new(
            Microsoft.Extensions.Options.Options.Create(options),
            clock,
            nonceStore,
            nonceFactory ?? (() => Guid.NewGuid().ToString("N")));

    private static MediaTokenRequest Request(string assetKey, MediaScope scope, TimeSpan ttl)
        => new(Org, assetKey, scope, ttl, SingleUse: scope == MediaScope.VisionAnalyze);

    private static string Mint(HmacMediaUrlSigner signer, string assetKey, MediaScope scope)
    {
        var ttl = scope == MediaScope.VisionAnalyze
            ? TimeSpan.FromSeconds(600)
            : TimeSpan.FromSeconds(900);
        return signer.Mint(Request(assetKey, scope, ttl), out _);
    }

    /// <summary>Changes one character to a different base64url character, keeping it decodable.</summary>
    private static string Flip(string value, int index)
    {
        var characters = value.ToCharArray();
        characters[index] = characters[index] == 'A' ? 'B' : 'A';
        return new string(characters);
    }

    /// <summary>A clock the expiry boundary is asserted against, so no case is time-flaky.</summary>
    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>The one claim per nonce, in memory, with a switch for the Redis-unavailable case.</summary>
    private sealed class RecordingNonceStore : IMediaTokenNonceStore
    {
        private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);

        public bool Unavailable { get; set; }

        public List<string> Claims { get; } = [];

        public Task<MediaNonceClaim> TryClaimAsync(string nonce, TimeSpan ttl, CancellationToken ct = default)
        {
            if (Unavailable)
            {
                return Task.FromResult(MediaNonceClaim.Unavailable);
            }

            if (!_claimed.Add(nonce))
            {
                return Task.FromResult(MediaNonceClaim.AlreadyClaimed);
            }

            Claims.Add(nonce);
            return Task.FromResult(MediaNonceClaim.Claimed);
        }
    }
}
