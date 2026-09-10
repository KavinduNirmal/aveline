using System.Security.Cryptography;
using System.Text;
using Aveline.Api.Modules.Integrations.Services;
using Xunit;

namespace Aveline.Api.Tests;

public class WebhookSignatureVerifierTests
{
    private const string AppSecret = "my-app-secret";

    private static string Sign(byte[] body, string secret = AppSecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        var body = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");
        Assert.True(WebhookSignatureVerifier.Verify(Sign(body), body, AppSecret));
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        var body = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");
        Assert.False(WebhookSignatureVerifier.Verify(Sign(body, "wrong-secret"), body, AppSecret));
    }

    [Fact]
    public void Verify_TamperedBody_ReturnsFalse()
    {
        var body = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");
        var tampered = Encoding.UTF8.GetBytes("{\"hello\":\"tampered\"}");
        Assert.False(WebhookSignatureVerifier.Verify(Sign(body), tampered, AppSecret));
    }

    [Fact]
    public void Verify_MissingPrefix_ReturnsFalse()
    {
        var body = Encoding.UTF8.GetBytes("body");
        Assert.False(WebhookSignatureVerifier.Verify("abc123", body, AppSecret));
    }

    [Fact]
    public void Verify_EmptySignature_ReturnsFalse()
    {
        var body = Encoding.UTF8.GetBytes("body");
        Assert.False(WebhookSignatureVerifier.Verify("", body, AppSecret));
    }

    [Fact]
    public void Verify_EmptyBody_ReturnsFalse()
    {
        Assert.False(WebhookSignatureVerifier.Verify("sha256=abc", [], AppSecret));
    }

    [Fact]
    public void Verify_MalformedHex_ReturnsFalse()
    {
        var body = Encoding.UTF8.GetBytes("body");
        Assert.False(WebhookSignatureVerifier.Verify("sha256=not-hex", body, AppSecret));
    }
}
