using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// The startup fail-fast rules for the privacy surface (plan §5.1), following the precedent of
/// <c>MediaOptionsValidator</c> (a half-configured signing key is a boot error, not a first-request
/// error) and <c>CredentialEncryptionService</c> (a present but unusable key throws).
/// </summary>
/// <remarks>
/// <b>Absence is deliberately not a boot failure.</b> An entirely absent
/// <c>Privacy:LinkSigningKey</c> logs a warning and boots, exactly as an absent
/// <c>Credentials:EncryptionKey</c> does today: the disclosure path checks
/// <see cref="IPrivacyLinkSigner.IsConfigured"/> and skips (leaving <c>DisclosureShownAt</c> NULL so
/// the next inbound message retries) rather than crashing. Making absence fatal would refuse to
/// boot every host that has not opted into the privacy surface - including the whole existing
/// integration-test fleet - which is a blast radius a signing key does not justify. A key that is
/// <i>present</i> (including present-and-blank) must be usable, and that is enforced everywhere.
/// </remarks>
public static class PrivacyOptionsValidator
{
    /// <summary>The key this validator owns; re-stated so a reader does not need the signer type.</summary>
    public const string LinkSigningKeyKey = PrivacyLinkSigner.ConfigKey;

    /// <summary>
    /// Validates the privacy configuration and throws <see cref="InvalidOperationException"/> when a
    /// set key cannot be used. Call once at startup, after configuration is bound.
    /// </summary>
    public static void ValidateOrThrow(IConfiguration configuration, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var encoded = configuration[LinkSigningKeyKey];

        if (encoded is null)
        {
            logger?.LogWarning(
                "{ConfigKey} is not configured. The first-contact disclosure cannot be sent and "
                + "will be retried on the next inbound message. Set it to a base64-encoded key of at "
                + "least {MinimumKeyBytes} bytes (generate with `openssl rand -base64 32`).",
                LinkSigningKeyKey,
                PrivacyLinkSigner.MinimumKeyBytes);
            return;
        }

        var error = DescribeKeyError(encoded);
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }
    }

    /// <summary>
    /// Returns the reason <paramref name="encoded"/> cannot sign a link, or <c>null</c> when it can.
    /// Exposed so the rule is testable without a host.
    /// </summary>
    public static string? DescribeKeyError(string? encoded)
    {
        if (encoded is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(encoded))
        {
            return $"{LinkSigningKeyKey} is blank. Remove it, or set it to a base64-encoded key of "
                   + $"at least {PrivacyLinkSigner.MinimumKeyBytes} bytes "
                   + "(generate with `openssl rand -base64 32`).";
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return $"{LinkSigningKeyKey} must be a valid base64 string.";
        }

        if (key.Length < PrivacyLinkSigner.MinimumKeyBytes)
        {
            return $"{LinkSigningKeyKey} must decode to at least "
                   + $"{PrivacyLinkSigner.MinimumKeyBytes} bytes to provide adequate HMAC strength.";
        }

        return null;
    }
}
