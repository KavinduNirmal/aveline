using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Infrastructure.Integrations;

public class InternalTokenAuthenticationOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// Authenticates requests bearing the <c>X-Internal-Token</c> header by matching it
/// against the configured <c>AgentService:InternalToken</c> secret.
/// </summary>
public class InternalTokenAuthenticationHandler(
    IOptionsMonitor<InternalTokenAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<InternalTokenAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "InternalToken";

    /// <summary>
    /// The role granted to an authenticated internal caller. Named here so consumers that need to
    /// recognise an internal principal (for example the health response writer, which discloses
    /// per-check detail only to insiders) do not repeat the literal.
    /// </summary>
    public const string RoleName = "InternalService";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(InternalServiceAuthHandler.HeaderName, out var tokenValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedToken = tokenValues.FirstOrDefault();
        if (string.IsNullOrEmpty(providedToken))
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty internal token."));
        }

        var configuredToken = configuration[InternalTokenSecurityGuard.ConfigKey];
        // Only a *known guessable* value is refused here, so test and development hosts using a
        // short throwaway token keep working. Production additionally refuses to boot with a weak
        // one (InternalTokenSecurityGuard), which is where the length/entropy bar lives.
        if (InsecureInternalTokens.RejectKnownPlaceholder(configuredToken) is { } reason)
        {
            Logger.LogError(
                "AgentService:InternalToken is insecure ({Reason}). Refusing to authenticate "
                + "internal callers.",
                reason);
            return Task.FromResult(AuthenticateResult.Fail("Internal token not configured or insecure on server."));
        }

        // Constant-time comparison to prevent timing attacks
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(providedToken);
        var configuredBytes = System.Text.Encoding.UTF8.GetBytes(configuredToken);

        if (System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes))
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "internal-service"),
                new Claim(ClaimTypes.Role, RoleName),
                new Claim("scope", "internal:all"),
            };

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid internal token."));
    }
}
