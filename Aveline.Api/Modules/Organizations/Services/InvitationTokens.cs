using System.Security.Cryptography;

namespace Aveline.Api.Modules.Organizations.Services;

/// <summary>
/// Generates opaque invitation codes and their one-way hashes. Only the hash is
/// persisted; the plaintext code is shown to the inviter exactly once.
/// </summary>
public static class InvitationTokens
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I/O/0/1

    /// <summary>Generates a random human-friendly code (default 12 chars).</summary>
    public static string GenerateCode(int length = 12)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Span<byte> buffer = stackalloc byte[length];
        RandomNumberGenerator.Fill(buffer);

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[buffer[i] % Alphabet.Length];
        }

        return new string(chars);
    }

    /// <summary>Returns the SHA-256 hex digest of a code, suitable for lookup/compare.</summary>
    public static string Hash(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }
}
