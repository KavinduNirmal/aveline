namespace Aveline.Api.Modules.Integrations.Services;

/// <summary>
/// AES-256-GCM encryption for tenant secrets. Ciphertext is stored in the form
/// <c>iv_hex:tag_hex:ciphertext_hex</c>. The only persistent secret is the 32-byte key
/// (read from <c>Credentials:EncryptionKey</c>, base64 encoded).
/// </summary>
public interface ICredentialEncryptionService
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/>, returning an <c>iv:tag:ciphertext</c> hex
    /// string. When <paramref name="associatedData"/> is supplied it is bound into the GCM
    /// tag (AAD) so the ciphertext cannot be replayed against a different row context
    /// (e.g. a different organization or integration type).
    /// </summary>
    string Encrypt(string plaintext, string? associatedData = null);

    /// <summary>
    /// Decrypts a value previously produced by <see cref="Encrypt(string, string?)"/>.
    /// The same <paramref name="associatedData"/> used at encryption time must be supplied;
    /// a mismatch throws, signalling tampering or a swapped blob.
    /// </summary>
    string Decrypt(string value, string? associatedData = null);
}
