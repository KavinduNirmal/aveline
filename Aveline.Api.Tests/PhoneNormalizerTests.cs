using Aveline.Api.Modules.CustomerConcierge.Common;

namespace Aveline.Api.Tests;

/// <summary>
/// Unit tests for <see cref="PhoneNormalizer"/> (Issue #161). Confirms that common
/// Sri Lankan phone formats normalise to E.164 (<c>+94xxxxxxxxx</c>) and that
/// unsupported input yields null.
/// </summary>
public class PhoneNormalizerTests
{
    [Theory]
    [InlineData("+94771234567", "+94771234567")]
    [InlineData("0771234567", "+94771234567")]
    [InlineData("94771234567", "+94771234567")]
    [InlineData("+94 77 123 4567", "+94771234567")]
    [InlineData("077 123 4567", "+94771234567")]
    [InlineData("071-234-5678", "+94712345678")]
    public void ToE164_ValidLocalFormats_ReturnsE164(string input, string expected)
    {
        Assert.Equal(expected, PhoneNormalizer.ToE164(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-phone")]
    [InlineData("+9477")]          // too short
    [InlineData("077123456")]      // 9 digits after 0 -> missing one digit
    [InlineData("+1 202 555 0100")] // foreign number not supported
    public void ToE164_InvalidInput_ReturnsNull(string? input)
    {
        Assert.Null(PhoneNormalizer.ToE164(input));
    }
}
