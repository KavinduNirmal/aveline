namespace Aveline.Api.Modules.Notifications.Models;

/// <summary>
/// A push-notification device token registered by a user (FCM). A user may have many
/// active tokens (one per device). Tokens are soft-deactivated rather than removed so a
/// re-registration can simply reactivate the row.
/// </summary>
public class UserDeviceToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    /// <summary>The FCM registration token for the device.</summary>
    public string Token { get; set; } = string.Empty;

    public DevicePlatform Platform { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}
