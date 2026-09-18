using Aveline.Api.Common.MultiTenancy;

namespace Aveline.Api.Modules.Home.Models;

/// <summary>
/// A human decision about a derived focus docket.
/// </summary>
/// <remarks>
/// The focus feed is recomputed on every read from facts that already exist (low
/// stock, customer events, paused agent runs); there is no task row whose state
/// could become terminal. This table is the only thing persisted: the decision.
/// A docket is filtered out of its caller's feed while a dismissal matches its
/// <see cref="SourceKey"/> and, when the feed supplies one, its
/// <see cref="ContentHash"/> — so a dismissal of "reorder the raw silk" does not
/// silently suppress a later, different low-stock docket for the same item.
/// </remarks>
public class FocusDismissal : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    /// <summary>The Aveline user who made the decision.</summary>
    public Guid UserId { get; set; }

    /// <summary>patron | logistics | wardrobe | commerce.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// The stable identifier of the underlying fact (an inventory item id, a
    /// customer-event id, an agent run id). It is what survives the feed being
    /// recomputed.
    /// </summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>signOff | approve | reject | acknowledge | markReady.</summary>
    public string Decision { get; set; } = string.Empty;

    /// <summary>
    /// A hash of the docket's content at the moment of the decision, or null when
    /// the docket carries nothing the human approved.
    /// </summary>
    public string? ContentHash { get; set; }

    public string? Note { get; set; }

    public DateTime DismissedAtUtc { get; set; } = DateTime.UtcNow;
}
