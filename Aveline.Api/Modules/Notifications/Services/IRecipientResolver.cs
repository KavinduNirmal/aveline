using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Services;

/// <summary>
/// Turns a <see cref="NotificationTarget"/> intent into concrete
/// <see cref="ResolvedRecipient"/>s using the organization membership tables.
/// </summary>
public interface IRecipientResolver
{
    Task<IReadOnlyList<ResolvedRecipient>> ResolveAsync(Notification notification, CancellationToken cancellationToken = default);
}
