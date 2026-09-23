using Aveline.Api.Infrastructure.Integrations;

namespace Aveline.Api.Tests;

/// <summary>
/// ADR-024, Decision 2. The API decides whether to create an order from the agent's reply, so the
/// reply's shape is a contract: a body it misreads means either a missing order or - worse - an order
/// drafted for a run that never asked for approval.
/// </summary>
public class AgentQueryResultTests
{
    private const string Paused = """
        {
          "status": "ok",
          "result": {
            "status": "pending_approval",
            "output": { "intent": "order_placement" },
            "metadata": { "model": "rule-based" }
          },
          "thread_id": "thread-1"
        }
        """;

    private const string Settled = """
        {
          "status": "ok",
          "result": { "status": "success", "output": {} },
          "thread_id": "thread-1"
        }
        """;

    [Fact]
    public void APauseEnvelope_IsRecognised() => Assert.True(AgentQueryResult.IsPaused(Paused));

    [Fact]
    public void AnOrdinaryAnswer_IsNotAPause() => Assert.False(AgentQueryResult.IsPaused(Settled));

    [Fact]
    public void TheAgentServicesCasing_DoesNotMatter()
        // The service emits camelCase; the transport wrapper's casing is not part of the contract.
        => Assert.True(AgentQueryResult.IsPaused("""{"Status":"ok","Result":{"Status":"PENDING_APPROVAL"}}"""));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // A truncated or non-JSON body must read as "not paused", because the write path is the thing
    // being guarded.
    [InlineData("{\"status\":\"ok\",\"result\":")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    public void AMalformedBody_IsNotAPause(string? body)
        => Assert.False(AgentQueryResult.IsPaused(body));

    [Fact]
    public void AnEnvelopeWithNoResult_IsNotAPause()
        => Assert.False(AgentQueryResult.IsPaused("""{"status":"ok"}"""));
}
