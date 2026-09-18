using System.Security.Cryptography;
using System.Text;

namespace Aveline.Api.Modules.ApiAccess.Domain;

/// <summary>A freshly generated API-key secret and its persisted derivatives.</summary>
public sealed record GeneratedApiKeySecret(string Plaintext, string Prefix, string Hash);

/// <summary>
/// Pure generation and comparison helpers for API-key secrets (FR-3.14).
/// </summary>
/// <remarks>
/// The secret never leaves this type except as the one-time <see cref="GeneratedApiKeySecret.Plaintext"/>.
/// Only the <see cref="GeneratedApiKeySecret.Prefix"/> (the indexed lookup path) and the
/// SHA-256 <see cref="GeneratedApiKeySecret.Hash"/> are persisted.
/// </remarks>
public static class ApiKeyCredentials
{
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int SecretLength = 32;

    /// <summary>Characters of the full secret stored as the lookup prefix.</summary>
    public const int PrefixLength = 16;

    /// <summary>Builds <c>avl_{live|test}_{32 base62}</c> and its prefix/hash.</summary>
    public static GeneratedApiKeySecret Generate(Models.ApiKeyEnvironment environment)
    {
        var label = environment == Models.ApiKeyEnvironment.Test ? "test" : "live";
        var chars = new char[SecretLength];
        for (var index = 0; index < SecretLength; index++)
        {
            chars[index] = Base62[RandomNumberGenerator.GetInt32(Base62.Length)];
        }

        var plaintext = $"avl_{label}_{new string(chars)}";
        return new GeneratedApiKeySecret(plaintext, PrefixOf(plaintext), Hash(plaintext));
    }

    /// <summary>The first <see cref="PrefixLength"/> characters, or the whole value when shorter.</summary>
    public static string PrefixOf(string plaintext) =>
        plaintext.Length <= PrefixLength ? plaintext : plaintext[..PrefixLength];

    /// <summary>Lowercase hexadecimal SHA-256 of the full secret.</summary>
    public static string Hash(string plaintext) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext))).ToLowerInvariant();

    /// <summary>Constant-time comparison of two hex digests.</summary>
    public static bool FixedTimeEquals(string left, string right)
    {
        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    }
}
