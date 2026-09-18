namespace Aveline.Api.Modules.Conversations.Models;

/// <summary>
/// Lifecycle state of a <see cref="Message"/>. Internal notes are <see cref="Published"/>;
/// outbound customer messages move through <see cref="Sent"/>/<see cref="Delivered"/>/
/// <see cref="Read"/>. <see cref="AwaitingSignOff"/> means the message is staged and waiting
/// for staff approval before it goes to the customer.
/// </summary>
public enum MessageStatus
{
    /// <summary>Being composed; not yet visible.</summary>
    Draft,

    /// <summary>Staged and waiting for staff approval before release.</summary>
    AwaitingSignOff,

    /// <summary>Visible in the Salon (internal note or approved content).</summary>
    Published,

    /// <summary>Sent to the customer channel.</summary>
    Sent,

    /// <summary>Delivered to the customer's device.</summary>
    Delivered,

    /// <summary>Read by the customer.</summary>
    Read,

    /// <summary>Delivery failed.</summary>
    Failed,

    /// <summary>Withdrawn before delivery.</summary>
    Cancelled,
}
