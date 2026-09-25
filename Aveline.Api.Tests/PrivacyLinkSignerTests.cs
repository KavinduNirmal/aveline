using Aveline.Api.Modules.Privacy.Services;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 3.1 (plan §5.1, DR-2): the permanent opt-out link is stateless and HMAC-signed over the
/// canonical payload <c>v1|{organizationId}</c>. There is no phone number in the payload, no
/// timestamp and no stored row, so the link cannot expire, and the OTP flow (Pr4) is what proves
/// which number is opting out.
/// </summary>
public class PrivacyLinkSignerTests
{
    private const string WebHost = "https://app.aveline.lk";

    /// <summary>A base64 key that decodes to exactly 32 bytes.</summary>
    private static readonly string ValidKey =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static PrivacyLinkSigner CreateSigner(string? key, string? webHost = WebHost)
    {
        var settings = new Dictionary<string, string?>();
        if (key is not null)
        {
            settings[PrivacyLinkSigner.ConfigKey] = key;
        }

        if (webHost is not null)
        {
            settings["App:BaseUrl"] = webHost;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new PrivacyLinkSigner(configuration);
    }

    [Fact]
    public void Sign_AndVerify_RoundTripsForTheSameOrganization()
    {
        var signer = CreateSigner(ValidKey);
        var orgId = Guid.NewGuid();

        var signature = signer.Sign(orgId);

        Assert.True(signer.Verify(orgId, PrivacyLinkSigner.CurrentVersion, signature));
    }

    [Fact]
    public void Verify_RejectsATamperedOrganizationId()
    {
        var signer = CreateSigner(ValidKey);

        var signature = signer.Sign(Guid.NewGuid());

        // The signature is valid for its own org, so a link pointing at a different boutique is
        // not a signature failure - but the HMAC does not cover that org, so it must be rejected.
        Assert.False(signer.Verify(Guid.NewGuid(), PrivacyLinkSigner.CurrentVersion, signature));
    }

    [Fact]
    public void Verify_RejectsATamperedVersion()
    {
        var signer = CreateSigner(ValidKey);
        var orgId = Guid.NewGuid();

        var signature = signer.Sign(orgId, "1");

        // The version is inside the signed payload, so "just bump the version" invalidates the
        // signature rather than silently reinterpreting the link under a new contract.
        Assert.False(signer.Verify(orgId, "2", signature));
    }

    [Fact]
    public void Verify_RejectsATamperedSignature()
    {
        var signer = CreateSigner(ValidKey);
        var orgId = Guid.NewGuid();

        var signature = signer.Sign(orgId);
        var tampered = FlipOneCharacter(signature);

        Assert.False(signer.Verify(orgId, PrivacyLinkSigner.CurrentVersion, tampered));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-signature")]
    public void Verify_RejectsAMissingOrGarbageSignature(string? signature)
    {
        var signer = CreateSigner(ValidKey);

        Assert.False(signer.Verify(Guid.NewGuid(), PrivacyLinkSigner.CurrentVersion, signature));
    }

    [Fact]
    public void Signature_IsUrlSafeBase64WithoutPadding()
    {
        var signer = CreateSigner(ValidKey);

        for (var i = 0; i < 32; i++)
        {
            var signature = signer.Sign(Guid.NewGuid());

            Assert.DoesNotContain('+', signature);
            Assert.DoesNotContain('/', signature);
            Assert.DoesNotContain('=', signature);
            Assert.Matches("^[A-Za-z0-9_-]+$", signature);
        }
    }

    [Fact]
    public void BuildOptOutUrl_HasTheDocumentedShapeAndCarriesNoPhoneNumber()
    {
        var signer = CreateSigner(ValidKey);

        AssertDocumentedOptOutUrl(signer, Guid.NewGuid());
    }

    /// <summary>
    /// The same assertion executed 200 times against fresh random organisations.
    /// </summary>
    /// <remarks>
    /// Before the structural rewrite, the no-phone-number check was `\+?\d{7,}` over the whole URL,
    /// which is probabilistic by construction: a Guid's 32 hex characters emit a seven-digit run by
    /// chance in a meaningful fraction of runs (observed at run 21 of an otherwise-unchanged test),
    /// and the base64url signature can too. Repeating it 200 times makes a regression impossible to
    /// miss rather than merely improbable, and costs one HMAC per draw.
    /// </remarks>
    [Theory]
    [MemberData(nameof(TwoHundredRuns))]
    public void BuildOptOutUrl_NeverCarriesAPhoneNumber(int _)
    {
        var signer = CreateSigner(ValidKey);

        AssertDocumentedOptOutUrl(signer, Guid.NewGuid());
    }

    public static IEnumerable<object[]> TwoHundredRuns =>
        Enumerable.Range(0, 200).Select(index => new object[] { index });

    /// <summary>
    /// Negative control: proves the structural no-phone-number check is not vacuous.
    /// </summary>
    /// <remarks>
    /// A phone number added to the URL as an extra query parameter must be rejected by the very
    /// assertion the real URL passes. The mutation lives here rather than in
    /// <c>PrivacyLinkSigner</c> so the privacy module is not touched to prove the test works.
    /// </remarks>
    [Fact]
    public void TheNoPhoneNumberAssertion_WouldCatchAnAddedPhoneParameter()
    {
        var signer = CreateSigner(ValidKey);
        var orgId = Guid.NewGuid();
        var mutated = $"{signer.BuildOptOutUrl(orgId)}&phone=0771234567";

        // `Assert.Equal` on the parameter-name set is what rejects it: the documented set is
        // exactly `o`, `v`, `s`.
        Assert.Throws<Xunit.Sdk.EqualException>(
            () => AssertDocumentedOptOutUrl(signer, orgId, mutated));
    }

    /// <summary>
    /// The documented opt-out URL, exactly: the three documented query parameters and no others.
    /// </summary>
    /// <remarks>
    /// DR-2 forbids a phone number in the link, and asserting that with a digit-run regex over the
    /// whole URL is <b>probabilistic</b>: `Guid.NewGuid().ToString("D")` and the base64url signature
    /// both produce seven-digit runs by chance, so an unchanged build failed intermittently. The
    /// intent is asserted structurally instead — the query carries exactly the documented parameter
    /// names, which leaves nowhere a phone number could travel, and no <c>+</c> (the E.164 marker)
    /// appears anywhere in the URL.
    /// </remarks>
    private static void AssertDocumentedOptOutUrl(
        PrivacyLinkSigner signer, Guid orgId, string? url = null)
    {
        url ??= signer.BuildOptOutUrl(orgId);

        Assert.StartsWith($"{WebHost}/privacy/opt-out?o={orgId:D}&v=1&s=", url, StringComparison.Ordinal);

        // Extract the signature and prove it verifies, so the URL is not merely the right shape.
        // The signature ends at the next `&`, so a parameter appended after it is still reachable
        // by the parameter-set assertion below rather than masked here.
        var signatureStart = url.IndexOf("&s=", StringComparison.Ordinal) + 3;
        var signatureEnd = url.IndexOf('&', signatureStart);
        var signature = signatureEnd < 0 ? url[signatureStart..] : url[signatureStart..signatureEnd];
        Assert.True(signer.Verify(orgId, "1", signature));

        // DR-2: no phone number, proved by construction rather than by a digit-run guess.
        var query = url[(url.IndexOf('?', StringComparison.Ordinal) + 1)..];
        var parameterNames = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=')[0])
            .ToArray();

        Assert.Equal(new[] { "o", "v", "s" }, parameterNames);
        Assert.DoesNotContain('+', url);
    }

    [Fact]
    public void BuildOptOutUrl_IsStableAcrossCallsAndTime()
    {
        var signer = CreateSigner(ValidKey);
        var orgId = Guid.NewGuid();

        // Nothing time-varying is in the payload, which is what makes the link permanent.
        Assert.Equal(signer.BuildOptOutUrl(orgId), signer.BuildOptOutUrl(orgId));
    }

    [Fact]
    public void Constructor_WithoutAKey_IsNotConfiguredAndSigningThrows()
    {
        var signer = CreateSigner(null);

        Assert.False(signer.IsConfigured);
        Assert.Throws<PrivacyLinkNotConfiguredException>(() => signer.Sign(Guid.NewGuid()));
    }

    [Fact]
    public void Constructor_WithAShortKey_Throws()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);

        var exception = Assert.Throws<InvalidOperationException>(() => CreateSigner(shortKey));
        Assert.Contains("32 bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_WithAMalformedKey_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateSigner("not-base64!!"));
        Assert.Contains("base64", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_WithAWhitespaceKey_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateSigner("   "));
    }

    [Fact]
    public void BuildDataPolicyUrl_CarriesTheOrganizationSlug()
    {
        var signer = CreateSigner(ValidKey);

        Assert.Equal(
            $"{WebHost}/privacy?org=emerald-boutique",
            signer.BuildDataPolicyUrl("emerald-boutique"));
    }

    private static string FlipOneCharacter(string value)
    {
        var index = value.Length / 2;
        var replacement = value[index] == 'A' ? 'B' : 'A';
        return value[..index] + replacement + value[(index + 1)..];
    }
}
