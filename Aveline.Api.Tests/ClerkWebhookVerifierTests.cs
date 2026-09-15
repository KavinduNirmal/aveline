using System.Text;
using Aveline.Api.Modules.Organizations.Webhooks;

namespace Aveline.Api.Tests;

/// <summary>Issue #207 — Clerk (Svix) webhook signature verification.</summary>
public class ClerkWebhookVerifierTests
{
    private static readonly string Secret =
        "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("aveline-webhook-test-signing-key"));
    private static readonly string OtherSecret =
        "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("a-different-signing-key-material!"));
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"type":"user.created"}""");
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static string Timestamp => Now.ToUnixTimeSeconds().ToString();

    [Fact]
    public void Verify_AcceptsAValidSignature()
    {
        var signature = ClerkWebhookVerifier.Sign(Secret, "msg_1", Timestamp, Body);

        Assert.True(ClerkWebhookVerifier.Verify(
            Secret, "msg_1", Timestamp, signature, Body, TimeSpan.FromMinutes(5), Now));
    }

    [Fact]
    public void Verify_RejectsATamperedBody()
    {
        var signature = ClerkWebhookVerifier.Sign(Secret, "msg_1", Timestamp, Body);

        Assert.False(ClerkWebhookVerifier.Verify(
            Secret, "msg_1", Timestamp, signature,
            Encoding.UTF8.GetBytes("""{"type":"user.deleted"}"""),
            TimeSpan.FromMinutes(5), Now));
    }

    [Fact]
    public void Verify_RejectsTheWrongSecret()
    {
        var signature = ClerkWebhookVerifier.Sign(Secret, "msg_1", Timestamp, Body);

        Assert.False(ClerkWebhookVerifier.Verify(
            OtherSecret, "msg_1", Timestamp, signature, Body,
            TimeSpan.FromMinutes(5), Now));
    }

    [Fact]
    public void Verify_RejectsAStaleTimestamp()
    {
        var timestamp = (Now - TimeSpan.FromMinutes(30)).ToUnixTimeSeconds().ToString();
        var signature = ClerkWebhookVerifier.Sign(Secret, "msg_1", timestamp, Body);

        Assert.False(ClerkWebhookVerifier.Verify(
            Secret, "msg_1", timestamp, signature, Body, TimeSpan.FromMinutes(5), Now));
    }

    [Theory]
    [InlineData(null, "msg_1", "1", "v1,x")]
    [InlineData("c2VjcmV0", null, "1", "v1,x")]
    [InlineData("c2VjcmV0", "msg_1", null, "v1,x")]
    [InlineData("c2VjcmV0", "msg_1", "1", null)]
    public void Verify_RejectsMissingInputs(string? secret, string? id, string? timestamp, string? signature)
    {
        Assert.False(ClerkWebhookVerifier.Verify(
            secret, id, timestamp, signature, Body, TimeSpan.FromMinutes(5), Now));
    }
}
