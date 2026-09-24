using System.Text.RegularExpressions;
using Aveline.Api.Modules.Privacy.Services;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 4.1's privacy helper: the one-way fingerprint every OTP counter key, cache key and audit
/// <c>ActorRef</c> uses. It exists so no key or audit row has to carry a number that identifies a
/// person, and so the counters still group deterministically across instances.
/// </summary>
public class PhoneFingerprintTests
{
    [Fact]
    public void Of_IsDeterministicAcrossCalls()
    {
        Assert.Equal(
            PhoneFingerprint.Of("+94771234567"),
            PhoneFingerprint.Of("+94771234567"));
    }

    [Fact]
    public void Of_DiffersForDifferentNumbers()
    {
        Assert.NotEqual(
            PhoneFingerprint.Of("+94771234567"),
            PhoneFingerprint.Of("+94771234568"));
    }

    [Fact]
    public void Of_NeverContainsTheDigitsOfTheNumber()
    {
        const string phone = "+94771234567";

        var fingerprint = PhoneFingerprint.Of(phone);

        Assert.DoesNotContain("94771234567", fingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("771234567", fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void Of_IsUrlSafeSoItCanSitInsideACacheKeyOrAnIdempotencyKey()
    {
        var fingerprint = PhoneFingerprint.Of("+94771234567");

        Assert.Matches(new Regex("^[A-Za-z0-9_-]+$"), fingerprint);
        Assert.DoesNotContain('=', fingerprint);
    }

    [Fact]
    public void Of_TrimsSurroundingWhitespace()
    {
        Assert.Equal(
            PhoneFingerprint.Of("+94771234567"),
            PhoneFingerprint.Of("  +94771234567  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_RejectsABlankNumber(string blank)
    {
        Assert.Throws<ArgumentException>(() => PhoneFingerprint.Of(blank));
    }

    [Fact]
    public void IpFingerprint_IsStableUrlSafeAndNeverTheAddress()
    {
        const string ip = "203.0.113.7";

        var fingerprint = OtpService.IpFingerprint(ip);

        Assert.Equal(fingerprint, OtpService.IpFingerprint(ip));
        Assert.DoesNotContain(ip, fingerprint, StringComparison.Ordinal);
        Assert.Matches(new Regex("^[A-Za-z0-9_-]+$"), fingerprint);
    }

    [Fact]
    public void IpFingerprint_TreatsAMissingAddressAsASingleSharedBucket()
    {
        // No address is not "no limit": unknown sources share one bucket rather than escaping the
        // counter entirely, which is what makes the fail-closed start budget meaningful.
        Assert.Equal(OtpService.IpFingerprint(null), OtpService.IpFingerprint(""));
        Assert.Equal(OtpService.IpFingerprint(null), OtpService.IpFingerprint("   "));
    }
}
