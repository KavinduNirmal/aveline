namespace Aveline.Api.Modules.Conversations.Models;

/// <summary>
/// Lifecycle state of a <see cref="Conversation"/>.
/// </summary>
public enum ConversationStatus
{
    /// <summary>Open and accepting messages.</summary>
    Active,

    /// <summary>Paused pending a human-in-the-loop approval.</summary>
    AwaitingSignOff,

    /// <summary>The underlying request has been resolved.</summary>
    Resolved,

    /// <summary>No longer surfaced in the active list.</summary>
    Archived,
}
