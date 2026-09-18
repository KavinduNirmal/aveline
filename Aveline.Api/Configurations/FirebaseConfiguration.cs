namespace Aveline.Api.Configurations;

/// <summary>
/// Centralizes Firebase Cloud Messaging configuration keys and readiness checks.
/// </summary>
public static class FirebaseConfiguration
{
    /// <summary>
    /// Returns whether Firebase is fully configured for FCM push delivery. Requires a
    /// project id and a credentials source (<c>Firebase:CredentialsPath</c> or the
    /// <c>GOOGLE_APPLICATION_CREDENTIALS</c> environment variable).
    /// </summary>
    public static bool IsConfigured(IConfiguration configuration)
    {
        var projectId = configuration["Firebase:ProjectId"];
        var credentialsPath = configuration["Firebase:CredentialsPath"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");

        return !string.IsNullOrWhiteSpace(projectId)
               && !string.IsNullOrWhiteSpace(credentialsPath);
    }
}
