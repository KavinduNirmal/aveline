namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// Canonical event type names for the conversation ("The Salon") slice, exchanged over the
/// Redis event bus (ADR-014). The agent service publishes these; the API consumes them and
/// becomes the system of record.
/// </summary>
public static class ConversationEvents
{
    /// <summary>A conversation (Salon) was created.</summary>
    public const string ConversationCreated = "conversation.created";

    /// <summary>An agent produced a message that should be persisted and broadcast.</summary>
    public const string MessageCreated = "message.created";

    /// <summary>An existing message changed (status, blocks).</summary>
    public const string MessageUpdated = "message.updated";

    /// <summary>Aveline's agentic-workflow state changed (thinking, searching, ...).</summary>
    public const string AgentStatus = "agent.status";
}
