using System.Security.Claims;
using System.Text.Encodings.Web;
using Aveline.Api.Modules.ApiAccess.Services;
using Aveline.Api.Modules.Organizations.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.ApiAccess.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>Claim types minted by the <c>ApiKey</c> scheme.</summary>
public static class ApiKeyClaimTypes
{
    public const string ApiKeyId = "api_key_id";
    public const string OrganizationId = "api_key_org";
    public const string Prefix = "api_key_prefix";

    /// <summary>One claim per granted permission; re-validated against the catalog at authorization time.</summary>
    public const string Scope = "scope";
}

/// <summary>
/// The third authentication scheme (A-10): resolves <c>X-Api-Key: avl_...</c> into a
/// principal whose <c>OrganizationId</c> is fixed to the owning organization (FR-3.17).
/// </summary>
/// <remarks>
/// A key of a suspended organization, a revoked key, an expired key, or an unknown secret
/// all fail authentication with 401 rather than 403, so the endpoint never reveals whether
/// the organization exists (BR-3.5). The scheme can be disabled entirely with
/// <c>ApiKeys:SchemeEnabled=false</c> for rollback.
/// </remarks>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!configuration.GetValue("ApiKeys:SchemeEnabled", true))
        {
            return AuthenticateResult.NoResult();
        }

        if (!Request.Headers.TryGetValue(HeaderName, out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        var presented = headerValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(presented))
        {
            return AuthenticateResult.Fail("Empty API key.");
        }

        var apiKeyService = Context.RequestServices.GetService(typeof(IApiKeyService)) as IApiKeyService;
        var organizationRepository =
            Context.RequestServices.GetService(typeof(IOrganizationRepository)) as IOrganizationRepository;
        if (apiKeyService is null || organizationRepository is null)
        {
            return AuthenticateResult.Fail("API key authentication is not configured.");
        }

        var key = await apiKeyService.AuthenticateAsync(presented.Trim(), Context.RequestAborted);
        if (key is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var organization = await organizationRepository.GetByIdAsync(key.OrganizationId, Context.RequestAborted);
        if (organization is null || !organization.IsActive || organization.SuspendedAt is not null)
        {
            // 401, never 403: do not leak organization existence or suspension state.
            return AuthenticateResult.Fail("The organization for this API key is not active.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, $"apikey:{key.Id}"),
            new(ApiKeyClaimTypes.ApiKeyId, key.Id.ToString()),
            new(ApiKeyClaimTypes.OrganizationId, key.OrganizationId.ToString()),
            new(ApiKeyClaimTypes.Prefix, key.Prefix),
        };
        claims.AddRange(key.Scopes.Select(scope => new Claim(ApiKeyClaimTypes.Scope, scope)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
