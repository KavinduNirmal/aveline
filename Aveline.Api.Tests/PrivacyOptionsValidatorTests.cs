using Aveline.Api.Modules.Privacy.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 3.1: the signing key rules, asserted against the validator directly. A key that cannot sign
/// is a configuration error, not a first-request error — the same posture
/// <c>MediaOptionsValidator</c> takes for <c>Media:SigningKey</c> and
/// <c>CredentialEncryptionService</c> takes for <c>Credentials:EncryptionKey</c>.
/// </summary>
public class PrivacyOptionsValidatorTests
{
    private static IConfiguration Configuration(string? key)
    {
        var settings = new Dictionary<string, string?>();
        if (key is not null)
        {
            settings[PrivacyLinkSigner.ConfigKey] = key;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    [Fact]
    public void AShortKeyIsRejected()
    {
        var key = Convert.ToBase64String(new byte[16]);

        var act = () => PrivacyOptionsValidator.ValidateOrThrow(Configuration(key), NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*at least 32 bytes*");
    }

    [Fact]
    public void AMalformedKeyIsRejected()
    {
        var act = () => PrivacyOptionsValidator.ValidateOrThrow(Configuration("still-not-base64"), NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*base64*");
    }

    [Fact]
    public void AWhitespaceKeyIsRejected()
    {
        var act = () => PrivacyOptionsValidator.ValidateOrThrow(Configuration("   "), NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*blank*");
    }

    [Fact]
    public void AValidKeyIsAccepted()
    {
        var key = Convert.ToBase64String(new byte[32]);

        var act = () => PrivacyOptionsValidator.ValidateOrThrow(Configuration(key), NullLogger.Instance);

        act.Should().NotThrow();
    }

    /// <summary>
    /// An entirely absent key does not refuse boot: it is the same posture as
    /// <c>Credentials:EncryptionKey</c>, whose absence is tolerated until the feature is used. This
    /// keeps the dozens of existing <c>WebApplicationFactory&lt;Program&gt;</c> hosts bootable; the
    /// signer still refuses to build a link, which is what makes the disclosure a no-op rather than
    /// a crash. Only a *present but unusable* key is a boot error.
    /// </summary>
    [Fact]
    public void AnAbsentKeyDoesNotRefuseBoot()
    {
        var act = () => PrivacyOptionsValidator.ValidateOrThrow(Configuration(null), NullLogger.Instance);

        act.Should().NotThrow();
    }
}
