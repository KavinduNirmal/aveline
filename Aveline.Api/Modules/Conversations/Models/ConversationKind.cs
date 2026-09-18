namespace Aveline.Api.Modules.Conversations.Models;

/// <summary>
/// The kind of a <see cref="Conversation"/>. Today only <see cref="Salon"/> is used:
/// the unified thread where staff and Aveline's agents participate. Other kinds are
/// reserved for future surfaces (announcements, digests).
/// </summary>
public enum ConversationKind
{
    /// <summary>The unified agent-to-staff thread (the "Salon").</summary>
    Salon,

    /// <summary>Reserved: a broadcast announcement.</summary>
    Announcement,

    /// <summary>Reserved: a scheduled digest.</summary>
    Digest,
}
