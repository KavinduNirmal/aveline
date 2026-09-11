using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Infrastructure.Integrations;

public class ScrapeTokenAuthenticationOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// Authenticates a Prometheus scraper presenting <c>Authorization: Bearer &lt;token&gt;</c>
/// where the token matches the optional <c>Metrics:ScrapeToken</c> configuration value.
/// When no token is configured the scheme yields no result, so it cannot authenticate by
/// accident and the internal-service scheme remains the only credential.
/// </summary>
public class ScrapeTokenAuthenticationHandler(
    IOptionsMonitor<ScrapeTokenAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<ScrapeTokenAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "ScrapeToken";

    private const string BearerPrefix = "Bearer ";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredToken = configuration["Metrics:ScrapeToken"];
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authorization)
            || !authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedToken = authorization[BearerPrefix.Length..].Trim();
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(providedToken);
        var configuredBytes = System.Text.Encoding.UTF8.GetBytes(configuredToken);

        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid scrape token."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "metrics-scraper"),
            new Claim(ClaimTypes.Role, "InternalService"),
        };

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)),
            SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
