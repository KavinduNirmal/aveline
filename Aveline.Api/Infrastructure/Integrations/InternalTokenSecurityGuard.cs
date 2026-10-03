namespace Aveline.Api.Infrastructure.Integrations;

/// <summary>
/// Startup guard for the internal service credential (<c>AgentService:InternalToken</c>),
/// following the precedent of <c>MetricsSecurityGuard</c> and <c>TelemetrySecurityGuard</c>.
/// Production must not boot with a token that the repository publishes.
/// </summary>
public static class InternalTokenSecurityGuard
{
    /// <summary>The configuration key shared with the authentication handler.</summary>
    public const string ConfigKey = "AgentService:InternalToken";

    /// <summary>
    /// Fails fast when Production is configured with a missing, placeholder, or low-entropy
    /// internal token. Other environments stay permissive so local development and tests need no
    /// secret, but an insecure value is still logged so it is visible before deployment.
    /// </summary>
    public static void EnsureInternalTokenForProduction(
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        var token = configuration[ConfigKey];
        var reason = InsecureInternalTokens.Reject(token);

        if (reason is null)
        {
            return;
        }

        var isProduction = string.Equals(
            environment.EnvironmentName, Environments.Production, StringComparison.OrdinalIgnoreCase);

        if (!isProduction)
        {
            logger?.LogWarning(
                "AgentService:InternalToken is insecure ({Reason}). This is tolerated outside "
                + "Production, but the API will refuse to boot with it in Production.",
                reason);
            return;
        }

        throw new InvalidOperationException(
            $"AgentService:InternalToken is insecure in Production: {reason}. Set "
            + $"INTERNAL_API_TOKEN (AgentService__InternalToken) to at least "
            + $"{InsecureInternalTokens.MinimumLength} random bytes. The token grants "
            + "role=InternalService and scope=internal:all over every /internal/* endpoint, and a "
            + "value published in this repository must never be accepted as a credential "
            + "(security assessment F-2.6).");
    }
}
