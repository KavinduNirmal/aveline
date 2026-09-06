using System.Text;
using Aveline.Api.Modules.Integrations.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aveline.Api.Tests;

public class CredentialEncryptionServiceTests
{
    private static ICredentialEncryptionService BuildService(byte[] key)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CredentialEncryptionService.ConfigKey] = Convert.ToBase64String(key),
            })
            .Build();
        return new CredentialEncryptionService(config);
    }

    private static byte[] Key(int fill = 1)
    {
        var key = new byte[32];
        Array.Fill(key, (byte)fill);
        return key;
    }

    [Fact]
    public void Constructor_WithoutKey_Throws()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.Throws<InvalidOperationException>(() => new CredentialEncryptionService(config));
    }

    [Fact]
    public void Constructor_WithShortKey_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CredentialEncryptionService.ConfigKey] = Convert.ToBase64String(new byte[16]),
            })
            .Build();
        Assert.Throws<InvalidOperationException>(() => new CredentialEncryptionService(config));
    }

    [Fact]
    public void Encrypt_RoundTrips_ThroughDecrypt()
    {
        var svc = BuildService(Key(7));
        const string plaintextValue = "EAAG-very-secret-whatsapp-token";

        var encrypted = svc.Encrypt(plaintextValue);

        Assert.NotEqual(plaintextValue, encrypted);
        Assert.Equal(plaintextValue, svc.Decrypt(encrypted));
    }

    [Fact]
    public void Encrypt_IsRandomized_EachCallProducesDifferentCiphertext()
    {
        var svc = BuildService(Key(9));

        var a = svc.Encrypt("same secret");
        var b = svc.Encrypt("same secret");

        Assert.NotEqual(a, b);
        Assert.Equal("same secret", svc.Decrypt(a));
        Assert.Equal("same secret", svc.Decrypt(b));
    }

    [Fact]
    public void Encrypt_UsesNonceTagCiphertextLayout()
    {
        var svc = BuildService(Key(3));

        var encrypted = svc.Encrypt("abc");

        var parts = encrypted.Split(':');
        Assert.Equal(3, parts.Length);
        // nonce = 12 bytes = 24 hex chars; tag = 16 bytes = 32 hex chars.
        Assert.Equal(24, parts[0].Length);
        Assert.Equal(32, parts[1].Length);
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsCryptographicException()
    {
        var svcA = BuildService(Key(1));
        var svcB = BuildService(Key(2)); // different key

        var encrypted = svcA.Encrypt("secret");

        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => svcB.Decrypt(encrypted));
    }

    [Fact]
    public void Decrypt_WithMalformedInput_ThrowsFormatException()
    {
        var svc = BuildService(Key(1));
        Assert.Throws<FormatException>(() => svc.Decrypt("not-an-encrypted-blob"));
    }
}
