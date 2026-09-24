using Aveline.Api.Modules.Privacy.Services;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 3.4 (plan §4.3): the first-contact disclosure body must carry all four required elements -
/// the boutique display name, the AI disclosure with the human-agent line, the data-policy URL and
/// the opt-out URL - plus the <c>STOP</c>/<c>HELP</c> line, and it must be versioned so the version
/// stamped on the consent row names exactly what was shown.
/// </summary>
public class DisclosureBodyBuilderTests
{
    private const string Boutique = "Emerald Boutique";
    private const string PolicyUrl = "https://app.aveline.lk/privacy?org=emerald";
    private const string OptOutUrl = "https://app.aveline.lk/privacy/opt-out?o=0b7a8d0a-0a1b-4c2d-8e3f-4a5b6c7d8e9f&v=1&s=abc";

    private static readonly DisclosureBodyBuilder Builder = new();

    [Fact]
    public void Build_ContainsTheBoutiqueDisplayName()
    {
        var body = Builder.Build(Boutique, PolicyUrl, OptOutUrl);

        Assert.Contains(Boutique, body.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ContainsTheAiDisclosureAndTheHumanAgentLine()
    {
        var body = Builder.Build(Boutique, PolicyUrl, OptOutUrl);

        // The AI disclosure: the customer must know an assistant, not a person, is answering.
        Assert.Contains("Aveline, an AI assistant", body.Text, StringComparison.Ordinal);
        // The human-agent line: a real member of the team reads the conversation and can step in.
        Assert.Contains("A real member of our team reads every conversation", body.Text, StringComparison.Ordinal);
        Assert.Contains("can step in", body.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ContainsTheDataPolicyAndOptOutUrls()
    {
        var body = Builder.Build(Boutique, PolicyUrl, OptOutUrl);

        Assert.Contains(PolicyUrl, body.Text, StringComparison.Ordinal);
        Assert.Contains(OptOutUrl, body.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ContainsTheStopAndHelpLine()
    {
        var body = Builder.Build(Boutique, PolicyUrl, OptOutUrl);

        Assert.Contains("Reply STOP", body.Text, StringComparison.Ordinal);
        Assert.Contains("HELP", body.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StampsTheVersion()
    {
        var body = Builder.Build(Boutique, PolicyUrl, OptOutUrl);

        Assert.Equal(Builder.CurrentVersion, body.Version);
        Assert.Equal("v1", body.Version);
    }

    [Fact]
    public void Build_MatchesTheFrozenSnapshot()
    {
        var body = Builder.Build(Boutique, PolicyUrl, OptOutUrl);

        const string expected =
            "Hello - this is Emerald Boutique, with help from Aveline, an AI assistant that "
            + "answers messages for us. A real member of our team reads every conversation and "
            + "can step in at any time.\n"
            + "\n"
            + $"What we store and why: {PolicyUrl}\n"
            + $"Opt out permanently, any time: {OptOutUrl}\n"
            + "\n"
            + "Reply STOP to opt out, or HELP for a human.";

        Assert.Equal(expected, body.Text);
    }

    [Fact]
    public void Build_DoesNotReuseTheStaffFacingSalonWelcome()
    {
        // ConversationService.cs:134-135 holds a *staff-facing* welcome ("Welcome to your Salon.
        // I'm Aveline, your boutique concierge... and I'll bring in Ava, Elle, or Lina") for a
        // different audience. The customer disclosure must not borrow it, because its promises are
        // about the staff console, not about what happens to the customer's own messages.
        var body = Builder.Build(Boutique, PolicyUrl, OptOutUrl);

        Assert.DoesNotContain("Welcome to your Salon", body.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("your boutique concierge", body.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Ava, Elle, or Lina", body.Text, StringComparison.Ordinal);
    }
}
