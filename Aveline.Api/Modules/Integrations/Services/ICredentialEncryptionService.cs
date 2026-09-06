namespace Aveline.Api.Modules.Integrations.Services;

/// <summary>
/// AES-256-GCM encryption for tenant secrets. Ciphertext is stored in the form
/// <c>iv_hex:tag_hex:ciphertext_hex</c>. The only persistent secret is the 32-byte key
/// (read from <c>Credentials:EncryptionKey</c>, base64 encoded).
/// </summary>
public interface ICredentialEncryptionService
{
    /// <summary>Encrypts plaintext, returning a <c>iv:tag:ciphertext</c> hex string.</summary>
    string Encrypt(string plaintext);

    /// <summary>Decrypts a value previously produced by <see cref="Encrypt"/>.</summary>
    string Decrypt(string value);
}
