using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.Models;

/// <summary>
/// A single message in a <see cref="Conversation"/>. Carries a render <see cref="Kind"/>,
/// an ordered JSON array of rich content blocks, an author (user/agent/system), an optional
/// <see cref="ReplyToMessageId"/> for threading, and audit ids linking it to a LangGraph run.
/// </summary>
public class Message
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ConversationId { get; set; }

    public AuthorKind AuthorKind { get; set; }

    /// <summary>Staff user id when <see cref="AuthorKind"/> is <see cref="AuthorKind.User"/>.</summary>
    public Guid? AuthorUserId { get; set; }

    /// <summary>Agent persona key when <see cref="AuthorKind"/> is <see cref="AuthorKind.Agent"/>.</summary>
    public string? AuthorAgentKey { get; set; }

    public MessageKind Kind { get; set; }

    /// <summary>Ordered array of typed content blocks, serialized as JSON (jsonb).</summary>
    public string ContentBlocksJson { get; set; } = "[]";

    /// <summary>
    /// Canonical SHA-256 hash of the content blocks, set when a <see cref="MessageKind.SignOff"/>
    /// is created. The human approves this exact payload; a decision is only valid if the hash
    /// still matches, so a later edit to the message cannot retroactively change what was approved.
    /// </summary>
    public string? ContentHash { get; set; }

    /// <summary>Parent message id for threaded replies.</summary>
    public Guid? ReplyToMessageId { get; set; }

    /// <summary>LangGraph run id for audit.</summary>
    public Guid? WorkflowRunId { get; set; }

    /// <summary>Correlation id linking this message to a broader workflow.</summary>
    public Guid? TraceId { get; set; }

    public MessageStatus Status { get; set; } = MessageStatus.Published;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Conversation? Conversation { get; set; }
}
