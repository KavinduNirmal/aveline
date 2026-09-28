using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The frozen media-token signer (migration plan §7.4–§7.5):
/// <c>base64url(utf8(payload)) "." base64url(HMAC-SHA256(key, ascii(payload_b64)))</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The MAC covers the entire encoded payload, not the identifier.</b> A scheme that signed only
/// the public id would let an attacker splice a valid MAC from asset A onto a payload naming asset
/// B; the splice case is a test. This is the property the format exists for, so the literal
/// vectors in <c>MediaTokenVectorTests</c> pin it byte for byte.
/// </para>
/// <para>
/// The verification order is fixed and each step has its own failure: MAC → <c>exp</c> → scope →
/// nonce. MAC first, so an attacker cannot distinguish "wrong key" from "expired" from "no such
/// asset" by response code.
/// </para>
/// <para>
/// The TTL policy is enforced <em>here</em>, not merely configured: a request above the per-scope
/// hard cap is clamped, so no caller can mint a 30-day token. Single-use is a property of the
/// <c>vision.analyze</c> scope and cannot be switched off by a caller.
/// </para>
/// </remarks>
public sealed class HmacMediaUrlSigner : IMediaUrlSigner
{
    /// <summary>The format version written into the <c>v</c> claim; a v2 can be added without ambiguity.</summary>
    internal const int FormatVersion = 1;

    /// <summary>The documented hard cap for <see cref="MediaScope.VisionAnalyze"/> (1800 s).</summary>
    internal static readonly TimeSpan VisionHardCap = TimeSpan.FromSeconds(1800);

    /// <summary>The documented hard cap for <see cref="MediaScope.AttachmentView"/> (3600 s).</summary>
    internal static readonly TimeSpan AttachmentHardCap = TimeSpan.FromSeconds(3600);

    private const int SigningKeyBytes = 32;

    private readonly MediaOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IMediaTokenNonceStore _nonceStore;
    private readonly Func<string> _nonceFactory;
    private readonly Lazy<byte[]> _key;

    public HmacMediaUrlSigner(
        IOptions<MediaOptions> options,
        TimeProvider timeProvider,
        IMediaTokenNonceStore nonceStore)
        : this(options, timeProvider, nonceStore, null)
    {
    }

    /// <summary>The test seam: the nonce source is injected so a vector can be pinned.</summary>
    internal HmacMediaUrlSigner(
        IOptions<MediaOptions> options,
        TimeProvider timeProvider,
        IMediaTokenNonceStore nonceStore,
        Func<string>? nonceFactory)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _nonceStore = nonceStore ?? throw new ArgumentNullException(nameof(nonceStore));
        _nonceFactory = nonceFactory ?? (() => Guid.NewGuid().ToString("N"));
        // Lazy so a host that never touches the protected tier is unaffected by a missing key;
        // MediaOptionsValidator refuses Provider=cloudinary without one at startup.
        _key = new Lazy<byte[]>(ResolveKey);
    }

    /// <inheritdoc />
    public string Mint(MediaTokenRequest request, out DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PublicId, nameof(request));

        var ttl = ResolveTtl(request);
        var now = _timeProvider.GetUtcNow();
        expiresAtUtc = now + ttl;

        // Single-use is a property of the scope, not a caller's preference: a vision token without
        // a nonce would be replayable, which has no legitimate use (migration plan §7.5).
        var singleUse = request.Scope == MediaScope.VisionAnalyze || request.SingleUse;
        var nonce = singleUse ? _nonceFactory() : null;

        var payload = BuildPayload(request, expiresAtUtc, nonce);
        var encodedPayload = Base64Url.EncodeToString(payload);
        var mac = HMACSHA256.HashData(_key.Value, Encoding.ASCII.GetBytes(encodedPayload));

        return $"{encodedPayload}.{Base64Url.EncodeToString(mac)}";
    }

    /// <inheritdoc />
    public MediaTokenValidation Verify(string token, MediaScope requiredScope, string? ipAddress = null)
    {
        // IP binding is deliberately not enabled by default: neither Azure Container Apps egress nor
        // the provider's fetcher addresses are stable, so a `c` claim would break the vision path
        // without a demonstrated win (migration plan §7.5). The parameter stays on the seam.
        _ = ipAddress;

        if (!TrySplit(token, out var encodedPayload, out var macSegment))
        {
            return MediaTokenValidation.Refused(MediaTokenFailure.Malformed);
        }

        // 1. The MAC, over the ASCII of the encoded payload, before any claim is read. Comparing
        //    fixed-time keeps the check free of timing side channels.
        byte[] presentedMac;
        try
        {
            presentedMac = Base64Url.DecodeFromChars(macSegment);
        }
        catch (FormatException)
        {
            return MediaTokenValidation.Refused(MediaTokenFailure.Malformed);
        }

        var expectedMac = HMACSHA256.HashData(_key.Value, Encoding.ASCII.GetBytes(encodedPayload));
        if (presentedMac.Length != expectedMac.Length
            || !CryptographicOperations.FixedTimeEquals(presentedMac, expectedMac))
        {
            return MediaTokenValidation.Refused(MediaTokenFailure.BadSignature);
        }

        // 2. The payload. It is only decoded after the MAC proves it was written by the key holder.
        MediaTokenClaims? claims;
        try
        {
            claims = ReadClaims(Base64Url.DecodeFromChars(encodedPayload));
        }
        catch (FormatException)
        {
            return MediaTokenValidation.Refused(MediaTokenFailure.Malformed);
        }
        catch (JsonException)
        {
            return MediaTokenValidation.Refused(MediaTokenFailure.Malformed);
        }

        if (claims is null || claims.Version != FormatVersion || claims.Scope is not { } scope)
        {
            return MediaTokenValidation.Refused(MediaTokenFailure.Malformed);
        }

        // 3. `exp`, allowing the configured clock skew.
        var now = _timeProvider.GetUtcNow();
        var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(claims.ExpiresAtUnixSeconds);
        var skew = TimeSpan.FromSeconds(Math.Max(0, _options.ClockSkewToleranceSeconds));
        if (now > expiresAtUtc + skew)
        {
            return MediaTokenValidation.Refused(MediaTokenFailure.Expired);
        }

        // 4. Scope.
        if (scope != requiredScope)
        {
            return MediaTokenValidation.Refused(MediaTokenFailure.ScopeMismatch);
        }

        // 5. The single-use nonce, last: a refused presentation must not burn a legitimate claim,
        //    and an unavailable store must fail closed.
        if (!string.IsNullOrEmpty(claims.Nonce))
        {
            var remaining = expiresAtUtc - now;
            if (remaining <= TimeSpan.Zero)
            {
                remaining = TimeSpan.FromSeconds(1);
            }

            var claim = _nonceStore.TryClaimAsync(claims.Nonce, remaining).ConfigureAwait(false)
                .GetAwaiter().GetResult();

            if (claim != MediaNonceClaim.Claimed)
            {
                return MediaTokenValidation.Refused(
                    claim == MediaNonceClaim.AlreadyClaimed
                        ? MediaTokenFailure.Replayed
                        : MediaTokenFailure.NonceStoreUnavailable);
            }
        }

        return new MediaTokenValidation(
            IsValid: true,
            Failure: MediaTokenFailure.None,
            PublicId: claims.PublicId,
            OrganizationId: claims.OrganizationId,
            Scope: scope);
    }

    /// <summary>
    /// The requested lifetime, bounded by the scope: the configured default when the request asks
    /// for none, and the scope's hard cap when the request asks for more.
    /// </summary>
    private TimeSpan ResolveTtl(MediaTokenRequest request)
    {
        var configuredDefault = request.Scope switch
        {
            MediaScope.VisionAnalyze => TimeSpan.FromSeconds(_options.VisionTokenTtlSeconds),
            MediaScope.AttachmentView => TimeSpan.FromSeconds(_options.AttachmentTokenTtlSeconds),
            _ => throw new InvalidOperationException(
                $"A '{MediaScopeNames.WireName(request.Scope)}' token cannot be minted: catalog "
                + "imagery is public (F-7/Q4) and carries no token (migration plan §7.5)."),
        };

        var requested = request.Ttl > TimeSpan.Zero ? request.Ttl : configuredDefault;
        var cap = request.Scope switch
        {
            MediaScope.VisionAnalyze => VisionHardCap,
            _ => AttachmentHardCap,
        };

        return requested > cap ? cap : requested;
    }

    private static byte[] BuildPayload(
        MediaTokenRequest request, DateTimeOffset expiresAtUtc, string? nonce)
    {
        // The claim order is part of the frozen format: the committed vector pins these bytes.
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", FormatVersion);
            writer.WriteString("p", request.PublicId);
            writer.WriteString("o", request.OrganizationId.ToString());
            writer.WriteString("s", MediaScopeNames.WireName(request.Scope));
            writer.WriteNumber("e", expiresAtUtc.ToUnixTimeSeconds());
            if (!string.IsNullOrEmpty(nonce))
            {
                writer.WriteString("n", nonce);
            }

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static MediaTokenClaims? ReadClaims(byte[] payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty("v", out var version) || !version.TryGetInt32(out var formatVersion))
        {
            return null;
        }

        if (!root.TryGetProperty("p", out var publicId) || publicId.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (!root.TryGetProperty("o", out var organization) || organization.ValueKind != JsonValueKind.String
            || !Guid.TryParse(organization.GetString(), out var organizationId))
        {
            return null;
        }

        if (!root.TryGetProperty("s", out var scopeName) || scopeName.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (!root.TryGetProperty("e", out var expiry) || !expiry.TryGetInt64(out var expiresAtUnixSeconds))
        {
            return null;
        }

        string? nonce = null;
        if (root.TryGetProperty("n", out var nonceElement) && nonceElement.ValueKind == JsonValueKind.String)
        {
            nonce = nonceElement.GetString();
        }

        return new MediaTokenClaims(
            formatVersion,
            publicId.GetString()!,
            organizationId,
            MediaScopeNames.TryParse(scopeName.GetString()),
            expiresAtUnixSeconds,
            nonce);
    }

    private static bool TrySplit(string? token, out string encodedPayload, out string macSegment)
    {
        encodedPayload = string.Empty;
        macSegment = string.Empty;

        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var separator = token.IndexOf('.');
        if (separator <= 0 || separator == token.Length - 1
            || token.IndexOf('.', separator + 1) >= 0)
        {
            return false;
        }

        encodedPayload = token[..separator];
        macSegment = token[(separator + 1)..];
        return true;
    }

    private byte[] ResolveKey()
    {
        var encoded = _options.SigningKey;
        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new InvalidOperationException(
                "Media:SigningKey must be configured before a media token can be minted or "
                + "verified. Set it to a base64-encoded 32-byte key (openssl rand -base64 32).");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Media:SigningKey must be a valid base64 string.");
        }

        if (key.Length != SigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"Media:SigningKey must decode to exactly {SigningKeyBytes} bytes.");
        }

        return key;
    }

    /// <summary>The decoded claims, carried from the payload to the validation result.</summary>
    private sealed record MediaTokenClaims(
        int Version,
        string PublicId,
        Guid OrganizationId,
        MediaScope? Scope,
        long ExpiresAtUnixSeconds,
        string? Nonce);
}
