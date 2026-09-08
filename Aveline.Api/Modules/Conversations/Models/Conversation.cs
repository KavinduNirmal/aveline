using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.Conversations.Models;

/// <summary>
/// A conversation (the "Salon"): the unified thread where staff and Aveline's agents
/// participate. Tenant-scoped. <see cref="ThreadId"/> is the LangGraph checkpoint key,
/// making this the single context anchor for the agent workflow.
/// </summary>
public class Conversation : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The boutique organization this conversation belongs to (tenant scope).</summary>
    public Guid OrganizationId { get; set; }

    public ConversationKind Kind { get; set; } = ConversationKind.Salon;

    /// <summary>Optional external customer this Salon concerns.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>LangGraph checkpoint key (the context anchor).</summary>
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>Optional channel reference (e.g. a WhatsApp conversation id).</summary>
    public string? ExternalRef { get; set; }

    public ConversationStatus Status { get; set; } = ConversationStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastMessageAt { get; set; }

    public Organization? Organization { get; set; }

    public ICollection<Message> Messages { get; set; } = [];
}
