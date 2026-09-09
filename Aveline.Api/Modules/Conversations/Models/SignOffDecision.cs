using Aveline.Api.Common.MultiTenancy;

namespace Aveline.Api.Modules.Conversations.Models;

/// <summary>
/// An immutable, out-of-band record of a human decision on a <see cref="MessageKind.SignOff"/>
/// message. Stored separately from the mutable <see cref="Message"/> row so that editing the
/// message later cannot retroactively change what was actually approved.
///
/// <para>
/// <see cref="ContentHash"/> is the canonical hash of the exact content blocks the human saw
/// and approved. A decision is only honoured while the message's current content still hashes
/// to the same value; if the payload was rewritten after display, the approval is void.
/// </para>
/// </summary>
public class SignOffDecision : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    /// <summary>The Salon the SignOff surfaced in.</summary>
    public Guid ConversationId { get; set; }

    /// <summary>The SignOff message that was decided.</summary>
    public Guid MessageId { get; set; }

    /// <summary>Canonical hash of the content blocks that were approved/rejected.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>True when approved; false when rejected.</summary>
    public bool Approved { get; set; }

    /// <summary>Staff user id who made the decision.</summary>
    public Guid? DecidedBy { get; set; }

    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
}
