namespace Aveline.Api.Modules.Conversations.DTOs;

/// <summary>
/// Aveline's current agentic-workflow state, broadcast to a Salon group over SignalR as
/// <c>ReceiveAgentState</c>. The <see cref="State"/> string values mirror the agent service's
/// <c>AgentState</c> enum and the TS/Dart client enums.
/// </summary>
public sealed record AgentStateDto(
    Guid ConversationId,
    string State,
    string? AgentKey,
    Guid? TraceId);
