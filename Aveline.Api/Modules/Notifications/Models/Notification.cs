namespace Aveline.Api.Modules.Notifications.Models;

/// <summary>
/// An immutable, type-agnostic notification to be dispatched. Carries the type, a
/// human-readable title/body, a free-form data payload, the target intent, and the
/// channels the caller would like used. Actual per-recipient channel selection is
/// decided by the channel router at dispatch time.
/// </summary>
/// <param name="Type">The notification type (see <see cref="NotificationType"/>).</param>
/// <param name="Title">Short human-readable title.</param>
/// <param name="Body">Human-readable body text.</param>
/// <param name="Target">Who should receive it (target intent).</param>
/// <param name="Data">Type-specific structured payload (e.g. order id, approval id).</param>
/// <param name="Channels">Channels the caller requests; the router may narrow these.</param>
public sealed record Notification(
    NotificationType Type,
    string Title,
    string Body,
    NotificationTarget Target,
    IReadOnlyDictionary<string, string?> Data,
    NotificationChannel Channels);
