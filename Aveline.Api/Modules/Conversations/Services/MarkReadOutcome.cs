namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// What happened to a read-marker write.
/// </summary>
/// <remarks>
/// The marker is monotonic, so a write that would move it backwards is not an error: it is
/// simply <see cref="Ignored"/>, and the endpoint still answers <c>204</c>. Only the two
/// refusals are distinguished, because they map to different status codes.
/// </remarks>
public enum MarkReadOutcome
{
    /// <summary>The marker advanced and was stored.</summary>
    Recorded,

    /// <summary>The incoming position is not after the stored one; nothing changed.</summary>
    Ignored,

    /// <summary>The conversation is not in this organization or is not visible to the caller.</summary>
    ConversationNotFound,

    /// <summary>The message does not belong to this conversation.</summary>
    MessageNotFound,
}
