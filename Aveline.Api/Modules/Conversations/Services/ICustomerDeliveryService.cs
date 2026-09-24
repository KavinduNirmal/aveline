using Aveline.Api.Modules.Conversations.DTOs;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// Why a block did not reach the customer. Every value is a state the associate can be told
/// about in a sentence, which is what makes it worth carrying to the client rather than
/// collapsing the whole family into one "failed".
/// </summary>
public enum DeliveryRefusal
{
    /// <summary>The conversation does not exist in this organization, or is not visible to the caller.</summary>
    ConversationNotFound,

    /// <summary>The thread is not bound to a customer, so there is nobody to deliver to.</summary>
    NoCustomer,

    /// <summary>The customer has no reachable handle (no channel reference and no phone number).</summary>
    NoChannelHandle,

    /// <summary>The boutique has not connected a customer channel at all.</summary>
    ChannelNotConnected,

    /// <summary>The boutique's channel is one this platform cannot send on yet (Instagram).</summary>
    ChannelUnsupported,

    /// <summary>The provider was reached and refused the send.</summary>
    ProviderRefused,
}

/// <summary>
/// The outcome of one attempt to put a block in front of the customer on their own channel.
/// A refusal is a value, not an exception: every one of them is an ordinary answer the endpoint
/// maps to a status code and the client renders verbatim.
/// </summary>
public sealed record DeliveryOutcome(
    bool Delivered,
    /// <summary>The channel the words actually left on (<c>WhatsApp</c>), when they did.</summary>
    string? Channel = null,
    /// <summary>The provider's own id for the outbound message, for support.</summary>
    string? ProviderMessageId = null,
    /// <summary>The row the delivery is recorded as, so the thread shows what went out.</summary>
    MessageDto? Message = null,
    DeliveryRefusal? Refusal = null,
    /// <summary>A sentence naming the reason, safe to show the associate.</summary>
    string? Detail = null)
{
    public static DeliveryOutcome Sent(string channel, string? providerMessageId, MessageDto message) =>
        new(true, channel, providerMessageId, message);

    public static DeliveryOutcome Refused(DeliveryRefusal refusal, string detail) =>
        new(false, Refusal: refusal, Detail: detail);
}

/// <summary>
/// Outbound customer delivery: the one path by which a block leaves the boutique for the
/// customer's own channel.
/// </summary>
/// <remarks>
/// This is deliberately **not** the same act as sending a staff note. A note is written into the
/// Salon, where it is Aveline's context and nothing else; it reaches no customer and, on a
/// client-bound thread, it wakes the agent. "Send to customer" and "Forward" mean the opposite:
/// the words leave over the customer's channel, and the salon thread records what went out. The
/// two are separate endpoints because they are separate promises, and conflating them is how a
/// console ends up telling an associate that a client was messaged when nobody was.
/// </remarks>
public interface ICustomerDeliveryService
{
    /// <summary>
    /// Delivers <paramref name="text"/> to the customer of <paramref name="conversationId"/> on
    /// their channel, and records it in the thread as a sent message.
    /// </summary>
    /// <param name="clientMessageId">
    /// Optional idempotency key for one composed delivery. A replay whose stored row already went
    /// out is answered from that row without a second send, so a retry after a timeout cannot
    /// message the customer twice.
    /// </param>
    Task<DeliveryOutcome> DeliverAsync(
        Guid organizationId,
        Guid userId,
        Guid conversationId,
        string text,
        Guid? clientMessageId = null,
        CancellationToken cancellationToken = default);
}
