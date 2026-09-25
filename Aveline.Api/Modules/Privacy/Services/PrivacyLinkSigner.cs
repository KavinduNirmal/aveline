using System.Security.Cryptography;
using System.Text;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// HMAC-SHA256 implementation of <see cref="IPrivacyLinkSigner"/> (plan §5.1). The key is read once
/// from <c>Privacy:LinkSigningKey</c> (base64, at least 32 bytes) and the web origin from the
/// existing <c>App:BaseUrl</c> key - the same key staff-invitation links use, so there is one
/// definition of "the web host" rather than two that can drift.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an absent key does not throw here.</b> The service is registered as a singleton and is
/// resolved on every inbound message that could trigger a disclosure. Throwing in the constructor
/// for a merely absent key would turn "the disclosure cannot be sent" into "every webhook request
/// 500s", which is the opposite of the webhook's record-what-we-can contract. A present but
/// unusable key is a different thing and <see cref="PrivacyOptionsValidator"/> refuses boot for it.
/// </para>
/// <para>
/// The signature is URL-safe base64 without padding, so it needs no encoding inside the query
/// string. Verification is constant-time.
/// </para>
/// </remarks>
public sealed class PrivacyLinkSigner : IPrivacyLinkSigner
{
    /// <summary>The configuration key holding the base64 signing key.</summary>
    public const string ConfigKey = "Privacy:LinkSigningKey";

    /// <summary>The link format/version this build signs. It is part of the signed payload.</summary>
    public const string CurrentVersion = "1";

    /// <summary>The minimum decoded key length. HMAC-SHA256 accepts less, but a short key is a bug.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>The customer-facing opt-out route the link points at.</summary>
    public const string OptOutPath = "/privacy/opt-out";

    /// <summary>The data-policy route the disclosure names.</summary>
    public const string DataPolicyPath = "/privacy";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly byte[]? _key;
    private readonly string _webHost;

    public PrivacyLinkSigner(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var encoded = configuration[ConfigKey];
        if (encoded is null)
        {
            // Absent means "not configured": signing refuses, the disclosure is skipped, and the
            // next inbound message retries. See the class remarks for why this is not a constructor
            // failure. A *blank* value is different - it was explicitly set to nothing, which is a
            // mistake - and the startup validator rejects it before this service is ever resolved.
            _key = null;
        }
        else if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new InvalidOperationException(
                $"{ConfigKey} is blank. Remove it, or set it to a base64-encoded key of at least "
                + $"{MinimumKeyBytes} bytes.");
        }
        else
        {
            byte[] key;
            try
            {
                key = Convert.FromBase64String(encoded);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"{ConfigKey} must be a valid base64 string.");
            }

            if (key.Length < MinimumKeyBytes)
            {
                throw new InvalidOperationException(
                    $"{ConfigKey} must decode to at least {MinimumKeyBytes} bytes.");
            }

            _key = key;
        }

        var webHost = configuration["App:BaseUrl"];
        _webHost = string.IsNullOrWhiteSpace(webHost) ? string.Empty : webHost.TrimEnd('/');
    }

    /// <inheritdoc />
    public bool IsConfigured => _key is not null;

    /// <inheritdoc />
    public string Sign(Guid organizationId, string? version = null)
    {
        if (_key is null)
        {
            throw new PrivacyLinkNotConfiguredException();
        }

        var canonical = CanonicalPayload(organizationId, version ?? CurrentVersion);
        using var hmac = new HMACSHA256(_key);
        var signature = hmac.ComputeHash(Utf8.GetBytes(canonical));
        return Base64UrlEncode(signature);
    }

    /// <inheritdoc />
    public bool Verify(Guid organizationId, string? version, string? signature)
    {
        if (_key is null || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = Sign(organizationId, version);
        return CryptographicOperations.FixedTimeEquals(
            Utf8.GetBytes(expected), Utf8.GetBytes(signature));
    }

    /// <inheritdoc />
    public string BuildOptOutUrl(Guid organizationId)
    {
        // Signing first means a host without a key fails before a URL exists, rather than handing
        // out a link whose signature is empty.
        var signature = Sign(organizationId, CurrentVersion);
        return $"{_webHost}{OptOutPath}?o={organizationId:D}&v={CurrentVersion}&s={signature}";
    }

    /// <inheritdoc />
    public string BuildDataPolicyUrl(string organizationSlug)
    {
        var query = string.IsNullOrWhiteSpace(organizationSlug)
            ? string.Empty
            : $"?org={Uri.EscapeDataString(organizationSlug)}";
        return $"{_webHost}{DataPolicyPath}{query}";
    }

    /// <summary>
    /// The exact bytes the HMAC covers. Written as <c>v1|{organizationId}</c>; the version is a
    /// literal part of the payload so it cannot be substituted after signing.
    /// </summary>
    internal static string CanonicalPayload(Guid organizationId, string version)
        => $"v{version}|{organizationId:D}";

    /// <summary>URL-safe base64 without padding, so the signature needs no query escaping.</summary>
    internal static string Base64UrlEncode(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
