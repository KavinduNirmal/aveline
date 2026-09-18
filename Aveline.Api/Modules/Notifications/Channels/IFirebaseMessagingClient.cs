namespace Aveline.Api.Modules.Notifications.Channels;

/// <summary>
/// Thin seam over the Firebase Cloud Messaging send API so the push channel can be unit
/// tested without real Firebase credentials.
/// </summary>
public interface IFirebaseMessagingClient
{
    Task SendAsync(
        string deviceToken,
        string title,
        string body,
        IReadOnlyDictionary<string, string?> data,
        CancellationToken cancellationToken = default);
}
