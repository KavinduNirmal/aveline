namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// A send reused a <c>clientMessageId</c> for a different message.
/// </summary>
/// <remarks>
/// A replay of the *same* message is not an error: it returns the stored row with <c>200</c>.
/// Reusing a key for different words means the client generated one key for two composed
/// messages, so the server cannot tell which one the caller meant. It refuses rather than
/// silently writing a second row under a key that promises exactly one.
/// </remarks>
public sealed class MessageIdempotencyConflictException : Exception
{
    public MessageIdempotencyConflictException(Guid conversationId, Guid clientMessageId)
        : base("This message id was already used for different content.")
    {
        ConversationId = conversationId;
        ClientMessageId = clientMessageId;
    }

    public Guid ConversationId { get; }

    public Guid ClientMessageId { get; }
}
