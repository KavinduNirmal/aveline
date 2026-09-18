namespace Aveline.Api.Modules.Notifications.Models;

/// <summary>
/// Describes the intended audience of a notification as an <em>intent</em>, not a
/// concrete list of users. The <see cref="Services.IRecipientResolver"/> turns this
/// into concrete recipients using the organization membership tables.
/// </summary>
/// <param name="OrganizationId">The boutique organization the notification is scoped to.</param>
/// <param name="Roles">
/// Optional canonical boutique role filter (e.g. <c>org:boutique_manager</c>,
/// <c>org:boutique_owner</c>). When null/empty and <paramref name="SpecificUserId"/> is
/// null, all active members of the organization are targeted.
/// </param>
/// <param name="SpecificUserId">
/// When set, targets exactly this user (who must be an active member of the
/// organization). Takes precedence over <paramref name="Roles"/>.
/// </param>
public sealed record NotificationTarget(
    Guid OrganizationId,
    IReadOnlyList<string>? Roles = null,
    Guid? SpecificUserId = null);
