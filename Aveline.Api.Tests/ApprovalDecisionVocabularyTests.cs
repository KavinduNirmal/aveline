namespace Aveline.Api.Tests;

using Aveline.Api.Modules.Commerce.Models;

/// <summary>
/// ADR-024, Decision 3. The API's HTTP verbs and the agent graph's vocabulary are two different sets,
/// and nothing used to compare them: the dashboard sent <c>approve</c> while the graph matched
/// <c>approved</c>, so a resume fell through every branch and settled nothing.
/// </summary>
/// <remarks>
/// The graph's values are the source of truth. These tests pin the literals on the C# side;
/// <c>agent-service/tests/test_hitl_resume.py</c> pins the same three on the Python side, and the two
/// are the only place the languages meet.
/// </remarks>
public class ApprovalDecisionVocabularyTests
{
    [Theory]
    [InlineData("approve", "approved")]
    [InlineData("APPROVE", "approved")]
    [InlineData(" approve ", "approved")]
    [InlineData("reject", "rejected")]
    [InlineData("REJECT", "rejected")]
    [InlineData("revise", "revised")]
    public void AnApiVerb_TranslatesToTheGraphsVocabulary(string verb, string expected)
        => Assert.Equal(expected, ApprovalDecisions.ToAgentDecision(verb));

    [Theory]
    [InlineData("approved")]
    [InlineData("approved!")]
    [InlineData("accept")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnythingElse_TranslatesToNothing(string? verb)
        // A guessed decision would settle a pause no human actually decided.
        => Assert.Null(ApprovalDecisions.ToAgentDecision(verb));

    [Fact]
    public void TheGraphVocabulary_IsExactlyThreeValues()
    {
        // Kept as an explicit list rather than a loop over constants: adding a fourth value has to be
        // a deliberate edit here, next to the note about the Python enum it must match.
        Assert.Equal("approved", ApprovalDecisions.AgentApproved);
        Assert.Equal("rejected", ApprovalDecisions.AgentRejected);
        Assert.Equal("revised", ApprovalDecisions.AgentRevised);
    }

    [Theory]
    [InlineData("approve", false)]
    [InlineData("reject", true)]
    [InlineData("revise", true)]
    public void ThePermissionSplit_IsUnchangedByTheNewVocabulary(string verb, bool requiresOrderManage)
        => Assert.Equal(requiresOrderManage, ApprovalDecisions.RequiresOrderManage(verb));
}
