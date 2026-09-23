using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aveline.Api.Infrastructure.Integrations;

/// <summary>
/// The slice of the agent service's reply the API reacts to (ADR-024, Decision 2).
/// </summary>
/// <remarks>
/// Only the status is modelled. The API does not read line items or prices back out of the agent's
/// answer: it derived them itself and sent them, and re-reading them would make the agent the
/// apparent author of the figures the approval rules gate on.
/// </remarks>
public sealed class AgentQueryResult
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("result")]
    public AgentResultEnvelope? Result { get; init; }

    /// <summary>The workflow's terminal status, or <c>null</c> when the body is not a reply.</summary>
    [JsonIgnore]
    public string? WorkflowStatus => Result?.Status;

    /// <summary>
    /// True when the agent stopped for human approval.
    /// </summary>
    /// <remarks>
    /// The literal is the agent service's published <c>AgentStatus</c> value, and the same string the
    /// agent's own telemetry maps to <c>PausedForApproval</c>. A body that does not parse, or does
    /// not carry a result, is "not paused" - the caller then does nothing, which is the safe default
    /// for a write path.
    /// </remarks>
    public static bool IsPaused(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AgentQueryResult>(body, Options);
            return string.Equals(parsed?.WorkflowStatus, "pending_approval", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>The transport wrapper around an agent workflow result.</summary>
public sealed class AgentResultEnvelope
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}
