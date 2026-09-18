namespace Aveline.Api.Modules.CustomerConcierge.Common;

/// <summary>
/// Normalizes Sri Lankan phone numbers to E.164 (<c>+94xxxxxxxxx</c>).
/// <para>
/// This is a pure value transformation used by the read-only customer lookup path. It is
/// deliberately applied at query time only and does <b>not</b> alter the <c>identify</c>
/// write path, so already-stored numbers are never rewritten by this type.
/// </para>
/// </summary>
public static class PhoneNormalizer
{
    private const string CountryCode = "94";
    private const int SubscriberLength = 9;

    /// <summary>
    /// Returns the E.164 form of <paramref name="raw"/>, or <c>null</c> when it is not a
    /// recognized Sri Lankan number (11 national digits, or a leading-0 10-digit local
    /// format). Separators, spaces and hyphens are ignored.
    /// </summary>
    public static string? ToE164(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());

        if (digits.Length == SubscriberLength + CountryCode.Length && digits.StartsWith(CountryCode, StringComparison.Ordinal))
        {
            // 94771234567 (from +94... or bare 94...)
            return "+" + digits;
        }

        if (digits.Length == SubscriberLength + 1 && digits.StartsWith('0'))
        {
            // 0771234567 -> +94771234567
            return "+" + CountryCode + digits[1..];
        }

        return null;
    }
}
