using Aveline.Api.Authorization;
using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Services;

/// <summary>
/// The recipient rule for each <see cref="NotificationType"/> (privacy plan §8.5.4, §11 Phase 6
/// item 6.1). <see cref="NotificationType"/>'s own doc comment promises that adding a type is "a
/// new enum value plus a recipient-resolver rule"; this is the rule table that promise names.
/// </summary>
/// <remarks>
/// <para>
/// The rules live here rather than at each producer because a producer that forgets its
/// <c>NotificationTarget.Roles</c> would otherwise broadcast to every member of the organization
/// (the resolver's documented no-filter behaviour). Centralising them makes "who receives a privacy
/// event" a single read rather than a convention across four call sites.
/// </para>
/// <para>
/// A target that names its own roles still wins: the rule is the default for an untyped target, not
/// a replacement for an explicit one. <c>AlertService</c>, for example, passes the roles configured
/// on the alert rule.
/// </para>
/// </remarks>
public static class NotificationRecipientRules
{
    /// <summary>
    /// The roles that own a legal or compliance-visible event: the boutique owner. Both the
    /// platform owner role (<c>owner</c>) and the boutique owner role
    /// (<c>org:boutique_owner</c>) are named, so a host without memberships still reaches an owner.
    /// </summary>
    public static IReadOnlyList<string> Owner { get; } = [Roles.Owner, Roles.BoutiqueOwner];

    /// <summary>The roles that may act on a customer's consent record: owner and manager.</summary>
    public static IReadOnlyList<string> OwnerOrManager { get; } =
        [Roles.Owner, Roles.BoutiqueOwner, Roles.BoutiqueManager];

    /// <summary>
    /// The default recipient roles for <paramref name="type"/>. A type with no explicit privacy
    /// rule keeps the resolver's long-standing behaviour - every member with an active boutique
    /// role - so an operational notification is not silently narrowed to management.
    /// </summary>
    public static IReadOnlyList<string> RolesFor(NotificationType type) => type switch
    {
        // A consent objection is acted on at the counter, by a manager as often as by the owner.
        NotificationType.ConsentRevoked => OwnerOrManager,

        // An erasure is a legal event. Plan §8.5.4: "arguably it should be the owner".
        NotificationType.DataDeleted => Owner,

        // A delivery failure is a compliance alarm, but the owner is the one who can fix the
        // boutique's integration; a manager reads the same alert from the integration screen.
        NotificationType.PrivacyDeliveryFailed => OwnerOrManager,

        // Every other type keeps the pre-existing "all active members" intent.
        _ => AllActiveMembers,
    };

    /// <summary>
    /// Every role the resolver can match an active membership against. It is the explicit form of
    /// the resolver's original "no role filter" behaviour, kept for a type whose rule is not listed
    /// above rather than leaving it to an empty list.
    /// </summary>
    private static IReadOnlyList<string> AllActiveMembers { get; } = Roles.StaffAccess;
}
