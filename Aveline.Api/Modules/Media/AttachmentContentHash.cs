using System.Security.Cryptography;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The byte-level SHA-256 identity of a stored asset, as lowercase hex (64 characters). This is
/// the value <c>MessageAttachments.ContentHash</c> is written from, and the same value the Elle
/// workstream consumes as <c>VisionAnalysis.ImageSha256</c> — one identity, one convention
/// (strategy §9).
/// </summary>
/// <remarks>
/// <c>ContentHash.Sha256Hex</c> (<c>Modules/Conversations/Services/ContentHash.cs:72-76</c>) is the
/// format precedent: <see cref="Convert.ToHexString(byte[])"/> then lowercase. That helper hashes
/// canonicalised content blocks, not raw bytes, so a byte-level helper is genuinely new — but the
/// hex casing and length are deliberately identical, because two conventions for one identity
/// would silently break the cross-workstream contract.
/// </remarks>
public static class AttachmentContentHash
{
    /// <summary>
    /// The lowercase-hex SHA-256 of <paramref name="bytes"/>. Acronym-spelled name and a span
    /// parameter keep this identical for a caller holding a <c>byte[]</c> or a slice of a stream.
    /// </summary>
    public static string Compute(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
