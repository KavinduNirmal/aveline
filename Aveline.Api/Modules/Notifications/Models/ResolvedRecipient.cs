using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.Notifications.Models;

/// <summary>
/// A concrete recipient resolved from a <see cref="NotificationTarget"/>, carrying the
/// per-user preferences the channel router needs to decide which channels apply.
/// </summary>
/// <param name="UserId">The Aveline user id.</param>
/// <param name="Email">The user's email address (null if unknown).</param>
/// <param name="PushEnabled">Whether the user has opted in to push notifications.</param>
/// <param name="ContactPreference">The user's preferred contact channel.</param>
/// <param name="DeviceTokens">Active FCM device tokens for the user (populated once the device registry lands).</param>
public sealed record ResolvedRecipient(
    Guid UserId,
    string? Email,
    bool PushEnabled,
    ContactPreferences ContactPreference,
    IReadOnlyList<string> DeviceTokens);
