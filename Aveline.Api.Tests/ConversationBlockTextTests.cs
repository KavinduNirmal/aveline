using Aveline.Api.Modules.Conversations.Services;

namespace Aveline.Api.Tests;

/// <summary>
/// Unit tests for <see cref="ConversationBlockText"/> — the pure seam that flattens stored
/// message blocks into the prose an agent reads as its transcript (ADR-023, W1.1).
/// </summary>
/// <remarks>
/// Blocks are heterogeneous and the failure modes are quiet (a wrong extraction feeds the agent a
/// misleading transcript rather than throwing), so the cases are pinned explicitly.
/// </remarks>
public class ConversationBlockTextTests
{
    [Theory]
    [InlineData("""[{"type":"text","text":"Hello there."}]""", "Hello there.")]
    [InlineData("""[{"type":"client_message","from":"94763475058","text":"Any pinkish gowns?"}]""",
        "Any pinkish gowns?")]
    [InlineData("""[{"type":"choice","prompt":"Which customer did you mean?"}]""",
        "Which customer did you mean?")]
    public void Flatten_ExtractsProseFromEachTextBearingBlock(string json, string expected) =>
        ConversationBlockText.Flatten(json).Should().Be(expected);

    [Fact]
    public void Flatten_JoinsMultipleProseBlocksInOrder()
    {
        const string json = """
            [{"type":"text","text":"first"},{"type":"choice","prompt":"second"}]
            """;

        ConversationBlockText.Flatten(json).Should().Be("first\nsecond");
    }

    [Fact]
    public void Flatten_ReturnsNullForBlocksWithNoProse()
    {
        // An image/attachment carries no readable text. It must not become an invented
        // description, because the agent would then "read" content it cannot actually see.
        const string json = """
            [{"type":"attachment","url":"https://example.test/a.jpg","contentType":"image/jpeg"}]
            """;

        ConversationBlockText.Flatten(json).Should().BeNull();
    }

    [Fact]
    public void Flatten_SkipsNonProseBlocksButKeepsProseSiblings()
    {
        const string json = """
            [{"type":"attachment","url":"https://example.test/a.jpg"},
             {"type":"text","text":"here is the piece I meant"}]
            """;

        ConversationBlockText.Flatten(json).Should().Be("here is the piece I meant");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("""{"type":"text","text":"wrapped in an object, not an array"}""")]
    [InlineData("[]")]
    [InlineData("""[{"type":"text","text":""}]""")]
    [InlineData("""[{"type":"text"}]""")]
    [InlineData("""[{"type":"text","text":123}]""")]
    [InlineData("""["a bare string, not a block"]""")]
    public void Flatten_ReturnsNullForEmptyMalformedOrNonArrayPayloads(string? json) =>
        ConversationBlockText.Flatten(json).Should().BeNull();
}
