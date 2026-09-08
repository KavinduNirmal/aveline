using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;

namespace Aveline.Api.Modules.Notifications.Services;

/// <summary>
/// Resolves <see cref="NotificationTarget"/> intents against the organization membership
/// tables. Only <see cref="MembershipStatus.Active"/> memberships are considered.
/// </summary>
public sealed class OrganizationRecipientResolver : IRecipientResolver
{
    private readonly IOrganizationRepository _organizations;
    private readonly IUserRepository _users;

    public OrganizationRecipientResolver(IOrganizationRepository organizations, IUserRepository users)
    {
        _organizations = organizations;
        _users = users;
    }

    public async Task<IReadOnlyList<ResolvedRecipient>> ResolveAsync(
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        var target = notification.Target;

        // A specific user takes precedence over role/all-member targeting.
        if (target.SpecificUserId is Guid userId)
        {
            var membership = await _organizations.GetMembershipAsync(target.OrganizationId, userId, cancellationToken);
            if (membership is null || membership.Status != MembershipStatus.Active)
            {
                return [];
            }

            var user = await _users.GetByIdAsync(userId, cancellationToken);
            return user is null ? [] : [ToRecipient(user)];
        }

        var members = await _organizations.GetActiveMembersAsync(target.OrganizationId, cancellationToken);
        var filtered = target.Roles is { Count: > 0 }
            ? members.Where(m => target.Roles.Contains(m.BoutiqueRole))
            : members;

        var recipients = new List<ResolvedRecipient>();
        foreach (var member in filtered)
        {
            if (member.User is not null)
            {
                recipients.Add(ToRecipient(member.User));
            }
        }

        return recipients;
    }

    private static ResolvedRecipient ToRecipient(User user) => new(
        user.Id,
        user.Email,
        user.PushNotificationsEnabled,
        user.ContactPreference,
        []);
}
