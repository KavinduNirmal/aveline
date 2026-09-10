using Aveline.Api.Modules.Conversations.Services;
using Xunit;

namespace Aveline.Api.Tests;

public class ContentHashTests
{
    [Fact]
    public void Compute_IsStableAcrossKeyOrder()
    {
        var a = ContentHash.Compute("""[{"type":"sign_off","amount":48000,"approvalId":"a-1"}]""");
        var b = ContentHash.Compute("""[{"approvalId":"a-1","amount":48000,"type":"sign_off"}]""");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_IsStableAcrossWhitespace()
    {
        var a = ContentHash.Compute("""[{"type":"sign_off","amount":48000}]""");
        var b = ContentHash.Compute("""
            [
              { "type": "sign_off", "amount": 48000 }
            ]
            """);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_ChangesWhenValueChanges()
    {
        var a = ContentHash.Compute("""[{"type":"sign_off","amount":48000}]""");
        var b = ContentHash.Compute("""[{"type":"sign_off","amount":99999}]""");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_IsDeterministic()
    {
        var json = """[{"type":"sign_off","approvalId":"a-1","amount":48000}]""";
        Assert.Equal(ContentHash.Compute(json), ContentHash.Compute(json));
    }

    [Fact]
    public void Compute_HandlesEmptyArray()
    {
        Assert.False(string.IsNullOrWhiteSpace(ContentHash.Compute("[]")));
    }
}
