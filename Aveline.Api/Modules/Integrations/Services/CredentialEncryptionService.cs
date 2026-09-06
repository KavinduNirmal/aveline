using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Integrations.Services;

/// <summary>
/// AES-256-GCM implementation of <see cref="ICredentialEncryptionService"/>.
/// The 32-byte key is read once from <c>Credentials:EncryptionKey</c> (base64).
/// Each secret gets a fresh 12-byte random nonce; the output is
/// <c>nonce_hex:tag_hex:ciphertext_hex</c>.
/// </summary>
public sealed class CredentialEncryptionService : ICredentialEncryptionService
{
    public const string ConfigKey = "Credentials:EncryptionKey";
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly byte[] _key;

    public CredentialEncryptionService(IConfiguration configuration)
    {
        var encoded = configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new InvalidOperationException(
                $"{ConfigKey} is not configured. Set it to a base64-encoded 32-byte key " +
                "(generate with `openssl rand -base64 32`).");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{ConfigKey} must be a valid base64 string.");
        }

        if (key.Length != KeySizeBytes)
        {
            throw new InvalidOperationException($"{ConfigKey} must decode to exactly {KeySizeBytes} bytes.");
        }

        _key = key;
    }

    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(_key, TagSizeBytes);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return $"{Convert.ToHexString(nonce)}:{Convert.ToHexString(tag)}:{Convert.ToHexString(ciphertext)}";
    }

    public string Decrypt(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var parts = value.Split(':', 3);
        if (parts.Length != 3)
        {
            throw new FormatException("Encrypted value is malformed; expected nonce:tag:ciphertext.");
        }

        var nonce = Convert.FromHexString(parts[0]);
        var tag = Convert.FromHexString(parts[1]);
        var ciphertext = Convert.FromHexString(parts[2]);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
