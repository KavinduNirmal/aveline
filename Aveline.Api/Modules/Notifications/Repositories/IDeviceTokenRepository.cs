using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Repositories;

public interface IDeviceTokenRepository
{
    /// <summary>
    /// Registers or refreshes a device token. If a row already exists for the token it is
    /// reactivated and its <see cref="UserDeviceToken.LastSeenAt"/> updated; otherwise a
    /// new active row is added.
    /// </summary>
    Task UpsertAsync(UserDeviceToken token, CancellationToken cancellationToken = default);

    /// <summary>Soft-deactivates the token belonging to the given user (no-op if absent).</summary>
    Task DeactivateAsync(Guid userId, string token, CancellationToken cancellationToken = default);

    /// <summary>Returns the active device tokens registered for a user.</summary>
    Task<IReadOnlyList<string>> ListActiveTokensAsync(Guid userId, CancellationToken cancellationToken = default);
}
