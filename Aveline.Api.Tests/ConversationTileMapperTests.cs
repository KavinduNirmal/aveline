using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;

namespace Aveline.Api.Tests;

/// <summary>
/// The inbox row's derivation: the block-aware preview, the row's category (the block the
/// preview came from) and the actionable marker set.
/// </summary>
public class ConversationTileMapperTests
{
    private static Message MessageWith(
        string contentBlocksJson,
        MessageKind kind = MessageKind.Note,
        AuthorKind author = AuthorKind.Agent,
        string? agentKey = null) => new()
    {
        ConversationId = Guid.NewGuid(),
        Kind = kind,
        AuthorKind = author,
        AuthorAgentKey = agentKey,
        ContentBlocksJson = contentBlocksJson,
        Status = MessageStatus.Published,
    };

    private static string Blocks(params string[] blocks) => "[" + string.Join(",", blocks) + "]";

    // ---------------------------------------------------------------- preview per block type

    [Theory]
    [InlineData("""{"type":"text","text":"Hello there"}""", "text", "Hello there")]
    // The case the "first text-bearing block" rule fails: an inbound message is a block whose
    // type is client_message, not text.
    [InlineData("""{"type":"client_message","from":"+94","text":"Can the wine silk be taken in?"}""", "client_message", "Can the wine silk be taken in?")]
    [InlineData("""{"type":"suggestion","text":"Hi Nadeesha, the wine silk can be taken in by Thursday."}""", "suggestion", "Hi Nadeesha, the wine silk can be taken in by Thursday.")]
    [InlineData("""{"type":"piece","name":"Wine silk saree","price":42000}""", "piece", "Wine silk saree")]
    [InlineData("""{"type":"look","name":"Evening in Galle","imageUrl":"x"}""", "look", "Evening in Galle")]
    [InlineData("""{"type":"look","imageUrl":"x"}""", "look", "A new look")]
    [InlineData("""{"type":"at_a_glance","columns":["Category","Content"],"rows":[["fit","tall"],["colour","wine"],["event","wedding"]]}""", "at_a_glance", "3 details")]
    [InlineData("""{"type":"payment","amount":48000,"status":"pending"}""", "payment", "Payment \u00b7 pending")]
    [InlineData("""{"type":"courier","carrier":"Pronto","status":"in transit"}""", "courier", "Pronto \u00b7 in transit")]
    [InlineData("""{"type":"sign_off","reason":"discount","amount":48000}""", "sign_off", "Approval needed")]
    [InlineData("""{"type":"choice","prompt":"Which one did you mean?","options":[]}""", "choice", "Which one did you mean?")]
    public void BuildPreview_MapsEachBlockType(string block, string expectedBlock, string expectedPreview)
    {
        var (preview, type) = ConversationTileMapper.BuildPreview(MessageWith(Blocks(block)));

        Assert.Equal(expectedPreview, preview);
        Assert.Equal(expectedBlock, type);
    }

    [Fact]
    public void BuildPreview_PaymentWithoutStatus_FallsBackToTheAmount()
    {
        var (preview, type) = ConversationTileMapper.BuildPreview(
            MessageWith("""[{"type":"payment","amount":48000}]"""));

        Assert.Equal("Payment \u00b7 LKR 48,000", preview);
        Assert.Equal("payment", type);
    }

    [Fact]
    public void BuildPreview_ReturnsNothing_ForABlockOnlyMessageWithNoKnownBlock()
    {
        var (preview, type) = ConversationTileMapper.BuildPreview(
            MessageWith("""[{"type":"mystery","text":"?"}]"""));

        Assert.Null(preview);
        Assert.Null(type);
    }

    [Fact]
    public void BuildPreview_ReturnsNothing_ForAnEmptyOrMissingBlockArray()
    {
        Assert.Null(ConversationTileMapper.BuildPreview(MessageWith("[]")).Preview);
        Assert.Null(ConversationTileMapper.BuildPreview(MessageWith("not json")).Preview);
        Assert.Null(ConversationTileMapper.BuildPreview(null).Preview);
    }

    [Fact]
    public void BuildPreview_TakesTheFirstMeaningfulBlock()
    {
        var (preview, type) = ConversationTileMapper.BuildPreview(MessageWith(Blocks(
            """{"type":"text","text":"Aveline's summary"}""",
            """{"type":"suggestion","text":"A draft" }""")));

        Assert.Equal("Aveline's summary", preview);
        Assert.Equal("text", type);
    }

    [Fact]
    public void BuildPreview_TruncatesToTheServerLimit()
    {
        var longText = new string('x', 200);
        var (preview, _) = ConversationTileMapper.BuildPreview(
            MessageWith(Blocks($$"""{"type":"text","text":"{{longText}}"}""")));

        Assert.NotNull(preview);
        Assert.Equal(ConversationTileMapper.PreviewMaxLength, preview!.Length);
    }

    // ---------------------------------------------------------------------------- markers

    [Fact]
    public void BuildMarkers_DeriveFromBlocks()
    {
        var suggestion = MessageWith("""[{"type":"suggestion","text":"draft"}]""");
        var choice = MessageWith("""[{"type":"choice","prompt":"which?"}]""");

        Assert.Equal(["draft"], ConversationTileMapper.BuildMarkers(suggestion, hasPendingSignOff: false));
        Assert.Equal(["choice"], ConversationTileMapper.BuildMarkers(choice, hasPendingSignOff: false));
        Assert.Equal([], ConversationTileMapper.BuildMarkers(
            MessageWith("""[{"type":"text","text":"nothing actionable"}]"""),
            hasPendingSignOff: false));
    }

    [Fact]
    public void BuildMarkers_APendingSignOffLeads_AndBothReachableMarkersFollow()
    {
        var draftAndChoice = MessageWith(Blocks(
            """{"type":"choice","prompt":"which?"}""",
            """{"type":"suggestion","text":"draft"}"""));

        Assert.Equal(["choice", "draft"], ConversationTileMapper.BuildMarkers(draftAndChoice, hasPendingSignOff: false));
        Assert.Equal(
            ["approval", "choice", "draft"],
            ConversationTileMapper.BuildMarkers(draftAndChoice, hasPendingSignOff: true));
        Assert.Equal(["approval"], ConversationTileMapper.BuildMarkers(null, hasPendingSignOff: true));
    }

    // ------------------------------------------------------------------------- author map

    [Theory]
    [InlineData(AuthorKind.User, "Staff")]
    [InlineData(AuthorKind.Agent, "Agent")]
    [InlineData(AuthorKind.System, "Customer")]
    public void AuthorFor_SpeaksTheClientsVocabulary(AuthorKind author, string expected)
    {
        Assert.Equal(expected, ConversationTileMapper.AuthorFor(MessageWith("[]", author: author)));
        Assert.Null(ConversationTileMapper.AuthorFor(null));
    }

    // ------------------------------------------------------------------------------ row

    [Fact]
    public void ToDto_CarriesTheRowFields_AndLeavesAnAbsentCustomerNameNull()
    {
        var concierge = new Conversation
        {
            Id = Guid.NewGuid(),
            Kind = ConversationKind.Salon,
            ThreadId = "thread-salon",
            Status = ConversationStatus.Active,
            ExternalRef = null,
        };
        var last = MessageWith(
            """[{"type":"client_message","from":"+94","text":"Can the wine silk be taken in?"}]""",
            kind: MessageKind.ClientMessage,
            author: AuthorKind.System);

        var dto = ConversationTileMapper.ToDto(new ConversationListRow(concierge, null, last, false));

        Assert.Equal(concierge.Id, dto.Id);
        Assert.Equal("Salon", dto.Kind);
        Assert.Null(dto.CustomerId);
        Assert.Null(dto.CustomerName);
        Assert.Null(dto.ExternalRef);
        Assert.Equal("client_message", dto.LastMessageBlock);
        Assert.Equal("ClientMessage", dto.LastMessageKind);
        Assert.Equal("Customer", dto.LastMessageAuthor);
        Assert.Null(dto.LastMessageAgentKey);
        Assert.Equal("Can the wine silk be taken in?", dto.LastMessagePreview);
        Assert.Empty(dto.Markers);
    }

    [Fact]
    public void ToDto_CarriesThePersonaAndTheMarkers()
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Kind = ConversationKind.Salon,
            CustomerId = Guid.NewGuid(),
            ExternalRef = "+94771234567",
            ThreadId = "thread-1",
            Status = ConversationStatus.AwaitingSignOff,
        };
        var last = MessageWith(
            """[{"type":"suggestion","text":"Hi Nadeesha..."}]""",
            author: AuthorKind.Agent,
            agentKey: "ava");

        var dto = ConversationTileMapper.ToDto(
            new ConversationListRow(conversation, "Nadeesha Perera", last, HasPendingSignOff: true));

        Assert.Equal("Nadeesha Perera", dto.CustomerName);
        Assert.Equal("+94771234567", dto.ExternalRef);
        Assert.Equal("ava", dto.LastMessageAgentKey);
        Assert.Equal(["approval", "draft"], dto.Markers);
    }
}
