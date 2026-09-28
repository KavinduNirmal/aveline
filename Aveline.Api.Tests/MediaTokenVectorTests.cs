using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Aveline.Api.Modules.Media;
using FluentAssertions;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U2.1 (lane L1) — <b>the freeze mechanism.</b> The two literals below are committed
/// <c>(key, payload, expected token)</c> triples for the media token format. The expected tokens
/// were derived independently of this implementation (HMAC-SHA256 over the ASCII of the
/// base64url-encoded payload, base64url, unpadded) and are pinned byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// A change to the format invalidates every outstanding token, so the format is not a caller's to
/// adjust: any change to the claim set, the claim order, the encoding or the MAC input makes
/// <see cref="TheVisionVectorIsFrozen"/> or <see cref="TheAttachmentVectorIsFrozen"/> fail loudly.
/// That is the point of the file. Do not "fix" a failure here by regenerating the literal; a
/// regeneration is a token-format change and belongs to a v2 (migration plan §7.4).
/// </para>
/// <para>
/// The clock and the nonce are injected, so the pin does not depend on the wall clock or on
/// randomness.
/// </para>
/// </remarks>
public class MediaTokenVectorTests
{
    // ---------------------------------------------------------------------------------------
    // The frozen triples
    // ---------------------------------------------------------------------------------------

    /// <summary>The frozen key: the 32 bytes 0x00..0x1F, base64 — the documented key shape.</summary>
    private const string FrozenKeyBase64 = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private static readonly Guid FrozenOrg = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string FrozenAssetKey =
        "image/authenticated:aveline/11111111-1111-1111-1111-111111111111"
        + "/conversations/22222222-2222-2222-2222-222222222222";

    private const long FrozenExpiryUnixSeconds = 1_761_000_000;

    private const string FrozenNonce = "frozen-nonce-0001";

    /// <summary>
    /// <c>vision.analyze</c>, single-use, 600 s. Payload:
    /// <c>{"v":1,"p":"image/authenticated:aveline/…/conversations/…","o":"11111111-…","s":"vision.analyze","e":1761000000,"n":"frozen-nonce-0001"}</c>
    /// </summary>
    private const string FrozenVisionToken =
        "eyJ2IjoxLCJwIjoiaW1hZ2UvYXV0aGVudGljYXRlZDphdmVsaW5lLzExMTExMTExLTExMTEtMTExMS0xMTEx"
        + "LTExMTExMTExMTExMS9jb252ZXJzYXRpb25zLzIyMjIyMjIyLTIyMjItMjIyMi0yMjIyLTIyMjIyMjIyMjIy"
        + "MiIsIm8iOiIxMTExMTExMS0xMTExLTExMTEtMTExMS0xMTExMTExMTExMTEiLCJzIjoidmlzaW9uLmFuYWx5"
        + "emUiLCJlIjoxNzYxMDAwMDAwLCJuIjoiZnJvemVuLW5vbmNlLTAwMDEifQ"
        + ".s4dZYu4Q8Idz0k91tnJH9M-azwZSC3-XoOg42qkeK4w";

    /// <summary>
    /// <c>attachment.view</c>, not single-use, 900 s, and therefore <b>no</b> <c>n</c> claim.
    /// </summary>
    private const string FrozenAttachmentToken =
        "eyJ2IjoxLCJwIjoiaW1hZ2UvYXV0aGVudGljYXRlZDphdmVsaW5lLzExMTExMTExLTExMTEtMTExMS0xMTEx"
        + "LTExMTExMTExMTExMS9jb252ZXJzYXRpb25zLzIyMjIyMjIyLTIyMjItMjIyMi0yMjIyLTIyMjIyMjIyMjIy"
        + "MiIsIm8iOiIxMTExMTExMS0xMTExLTExMTEtMTExMS0xMTExMTExMTExMTEiLCJzIjoiYXR0YWNobWVudC52"
        + "aWV3IiwiZSI6MTc2MTAwMDAwMH0"
        + ".eVm5CSRChkXo-B4_hhGwzrjWQnB2E_ifpn_XDBf3ZTQ";

    // ---------------------------------------------------------------------------------------
    // The pins
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TheVisionVectorIsFrozen()
    {
        var signer = CreateSigner(out var clock, FrozenNonce);
        // Minted 600 s before the frozen `exp`, so the payload's `e` is the pinned value exactly.
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(FrozenExpiryUnixSeconds - 600);

        var token = signer.Mint(
            new MediaTokenRequest(
                FrozenOrg, FrozenAssetKey, MediaScope.VisionAnalyze,
                TimeSpan.FromSeconds(600), SingleUse: true),
            out var expiresAtUtc);

        token.Should().Be(FrozenVisionToken);
        expiresAtUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(FrozenExpiryUnixSeconds));
    }

    [Fact]
    public void TheAttachmentVectorIsFrozen()
    {
        var signer = CreateSigner(out var clock, FrozenNonce);
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(FrozenExpiryUnixSeconds - 900);

        var token = signer.Mint(
            new MediaTokenRequest(
                FrozenOrg, FrozenAssetKey, MediaScope.AttachmentView,
                TimeSpan.FromSeconds(900), SingleUse: false),
            out var expiresAtUtc);

        token.Should().Be(FrozenAttachmentToken);
        expiresAtUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(FrozenExpiryUnixSeconds));
    }

    // ---------------------------------------------------------------------------------------
    // What the pin freezes, stated as assertions
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ThePinnedVisionPayloadCarriesExactlyTheFrozenClaims()
    {
        var payload = DecodePayload(FrozenVisionToken);

        payload.GetProperty("v").GetInt32().Should().Be(1);
        payload.GetProperty("p").GetString().Should().Be(FrozenAssetKey);
        payload.GetProperty("o").GetString().Should().Be(FrozenOrg.ToString());
        payload.GetProperty("s").GetString().Should().Be("vision.analyze");
        payload.GetProperty("e").GetInt64().Should().Be(FrozenExpiryUnixSeconds);
        payload.GetProperty("n").GetString().Should().Be(FrozenNonce);
        payload.EnumerateObject().Select(property => property.Name)
            .Should().Equal("v", "p", "o", "s", "e", "n");
    }

    [Fact]
    public void ThePinnedAttachmentPayloadCarriesNoNonce()
    {
        var payload = DecodePayload(FrozenAttachmentToken);

        payload.GetProperty("s").GetString().Should().Be("attachment.view");
        payload.TryGetProperty("n", out _).Should().BeFalse();
        payload.EnumerateObject().Select(property => property.Name)
            .Should().Equal("v", "p", "o", "s", "e");
    }

    [Fact]
    public void ThePinnedVisionTokenVerifies()
    {
        // The committed bytes round-trip through the verifier: the freeze proves the whole format,
        // not merely the minting side.
        var signer = CreateSigner(out var clock, FrozenNonce);
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(FrozenExpiryUnixSeconds - 1);

        var validation = signer.Verify(FrozenVisionToken, MediaScope.VisionAnalyze);

        validation.IsValid.Should().BeTrue();
        validation.PublicId.Should().Be(FrozenAssetKey);
        validation.OrganizationId.Should().Be(FrozenOrg);
        validation.Scope.Should().Be(MediaScope.VisionAnalyze);
    }

    [Fact]
    public void TheMacCoversTheEncodedPayloadNotTheIdentifier()
    {
        // Recompute the MAC independently and assert the token's MAC segment is exactly it: the
        // HMAC input is the ASCII of the encoded payload, so signing the identifier alone could
        // never produce this value (migration plan §7.4).
        var parts = FrozenVisionToken.Split('.');
        var key = Convert.FromBase64String(FrozenKeyBase64);
        var expectedMac = Base64Url.EncodeToString(
            System.Security.Cryptography.HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(parts[0])));

        parts[1].Should().Be(expectedMac);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static HmacMediaUrlSigner CreateSigner(out MutableClock clock, string nonce)
    {
        clock = new MutableClock(DateTimeOffset.FromUnixTimeSeconds(FrozenExpiryUnixSeconds));
        var options = new MediaOptions
        {
            SigningKey = FrozenKeyBase64,
            PublicBaseUrl = "https://api.aveline.lk",
        };

        return new HmacMediaUrlSigner(
            Microsoft.Extensions.Options.Options.Create(options),
            clock,
            new RecordingNonceStore(),
            () => nonce);
    }

    private static JsonElement DecodePayload(string token)
    {
        var payloadBytes = Base64Url.DecodeFromChars(token.Split('.')[0]);
        using var document = JsonDocument.Parse(payloadBytes);
        return document.RootElement.Clone();
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingNonceStore : IMediaTokenNonceStore
    {
        private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);

        public Task<MediaNonceClaim> TryClaimAsync(string nonce, TimeSpan ttl, CancellationToken ct = default)
            => Task.FromResult(_claimed.Add(nonce) ? MediaNonceClaim.Claimed : MediaNonceClaim.AlreadyClaimed);
    }
}
