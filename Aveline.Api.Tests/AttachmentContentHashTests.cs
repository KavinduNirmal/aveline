using System.Security.Cryptography;
using System.Text;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.Media;

namespace Aveline.Api.Tests;

/// <summary>
/// Unit U0.3 — byte-level SHA-256 for a stored asset. This value is the shared identity the Elle
/// workstream consumes as <c>VisionAnalysis.ImageSha256</c> (strategy §9), so the format is pinned
/// here: lowercase hex, exactly 64 characters, matching the <c>ContentHash.Sha256Hex</c> precedent.
/// </summary>
public class AttachmentContentHashTests
{
    // NIST FIPS 180-2 vectors.
    private const string AbcVector = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string EmptyVector = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Fact]
    public void Compute_MatchesTheKnownVectorForAbc()
    {
        var hash = AttachmentContentHash.Compute(Encoding.ASCII.GetBytes("abc"));

        Assert.Equal(AbcVector, hash);
    }

    [Fact]
    public void Compute_MatchesTheKnownVectorForEmptyInput()
    {
        Assert.Equal(EmptyVector, AttachmentContentHash.Compute([]));
        Assert.Equal(EmptyVector, AttachmentContentHash.Compute(Array.Empty<byte>()));
    }

    [Fact]
    public void Compute_IsLowercaseHexOfLength64()
    {
        var hash = AttachmentContentHash.Compute(Encoding.UTF8.GetBytes("a raw image body, not content blocks"));

        Assert.Equal(64, hash.Length);
        Assert.All(hash, c => Assert.True(c is >= '0' and <= '9' or >= 'a' and <= 'f', $"unexpected hex character '{c}'"));
    }

    [Fact]
    public void Compute_IsDeterministic()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x10, 0x20 };

        Assert.Equal(AttachmentContentHash.Compute(bytes), AttachmentContentHash.Compute(bytes));
    }

    [Fact]
    public void Compute_DifferentBytesProduceDifferentHashes()
    {
        var a = AttachmentContentHash.Compute(Encoding.UTF8.GetBytes("one image"));
        var b = AttachmentContentHash.Compute(Encoding.UTF8.GetBytes("another image"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_UsesTheSameHexConventionAsTheContentHashPrecedent()
    {
        // ContentHash.Sha256Hex is private, so the two helpers are compared through its documented
        // unparseable-payload fallback, which hashes the raw UTF-8 bytes. Equal output here means the
        // byte-level helper and the content-block helper agree on one convention — the §9 contract.
        const string notJson = "{ not a content-blocks payload";
        var bytes = Encoding.UTF8.GetBytes(notJson);

        Assert.Equal(ContentHash.Compute(notJson), AttachmentContentHash.Compute(bytes));
    }
}
