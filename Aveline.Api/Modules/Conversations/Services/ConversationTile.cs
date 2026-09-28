using Aveline.Api.Modules.Conversations.DTOs;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// The inbox tile plus the routing context its broadcast needs.
/// </summary>
/// <remarks>
/// Routing is a correctness requirement, not an optimisation: <c>org:{organizationId}</c> is
/// joined by every active member on connect, while the general Salon is owned by one user
/// (ADR-021). Sending a per-user Salon's tile to the org group would leak its existence to
/// colleagues, so the target group is chosen from <see cref="OwnerUserId"/>.
/// </remarks>
public sealed record ConversationTile(
    ConversationDto Tile,
    Guid OrganizationId,
    Guid? OwnerUserId);
