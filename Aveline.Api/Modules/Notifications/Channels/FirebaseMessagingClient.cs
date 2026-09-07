using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Notifications.Channels;

/// <summary>
/// Real FCM sender backed by the FirebaseAdmin SDK. Initializes the default
/// <see cref="FirebaseApp"/> once from configuration (<c>Firebase:ProjectId</c> and a
/// credentials source: <c>Firebase:CredentialsPath</c> or <c>GOOGLE_APPLICATION_CREDENTIALS</c>).
/// </summary>
public sealed class FirebaseMessagingClient : IFirebaseMessagingClient
{
    private readonly ILogger<FirebaseMessagingClient> _logger;
    private readonly FirebaseMessaging _messaging;

    public FirebaseMessagingClient(IConfiguration configuration, ILogger<FirebaseMessagingClient> logger)
    {
        _logger = logger;
        _messaging = FirebaseMessaging.GetMessaging(EnsureApp(configuration));
    }

    public async Task SendAsync(
        string deviceToken,
        string title,
        string body,
        IReadOnlyDictionary<string, string?> data,
        CancellationToken cancellationToken = default)
    {
        var message = new Message
        {
            Token = deviceToken,
            Notification = new Notification { Title = title, Body = body },
            Data = data.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!),
        };

        await _messaging.SendAsync(message, cancellationToken);
        _logger.LogInformation("FCM message sent to a device token.");
    }

    private static FirebaseApp EnsureApp(IConfiguration configuration)
    {
        if (FirebaseApp.DefaultInstance is not null)
        {
            return FirebaseApp.DefaultInstance;
        }

        var projectId = configuration["Firebase:ProjectId"];
        var credentialsPath = configuration["Firebase:CredentialsPath"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");

        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(credentialsPath))
        {
            throw new InvalidOperationException(
                "Firebase is not fully configured. Set Firebase:ProjectId and Firebase:CredentialsPath (or GOOGLE_APPLICATION_CREDENTIALS).");
        }

        return FirebaseApp.Create(new AppOptions
        {
            ProjectId = projectId,
            Credential = GoogleCredential.FromFile(credentialsPath),
        });
    }
}
