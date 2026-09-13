using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.ApiAccess.Authentication;
using Aveline.Api.Modules.ApiAccess.DTOs;
using Aveline.Api.Modules.ApiAccess.Models;
using Aveline.Api.Modules.ApiAccess.Services;
using Aveline.Api.Modules.Shared.Services;

namespace Aveline.Api.Modules.ApiAccess.Endpoints;

/// <summary>
/// Organization API-key management (FR-3.10–FR-3.13). Reads require <c>apikeys:view</c>;
/// create/revoke/delete require <c>apikeys:manage</c>. The plaintext secret appears only
/// in the 201 response.
/// </summary>
public static class ApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/orgs/{organizationId:guid}/api-keys").WithTags("API Keys");

        group.MapGet("", async (
            Guid organizationId, IApiKeyService apiKeys, CancellationToken ct) =>
        {
            var keys = await apiKeys.ListAsync(organizationId, ct);
            return Results.Ok(keys.Select(ApiKeyDto.From));
        }).RequireAuthorization(AuthorizationConfiguration.ApiKeysViewPolicy);

        group.MapPost("", async (
            Guid organizationId,
            CreateApiKeyRequest request,
            ClaimsPrincipal principal,
            IApiKeyService apiKeys,
            IUserService users,
            CancellationToken ct) =>
        {
            // An API key must never mint another key: it would allow a machine credential to
            // escalate its own scope and would have no user to attribute the creation to.
            var actorUserId = await ResolveUserIdAsync(principal, users, ct);
            if (actorUserId is null)
            {
                return Results.Json(
                    new { message = "An API key cannot create another API key." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (!TryParseEnvironment(request.Environment, out var environment))
            {
                return Results.BadRequest(new { message = "Environment must be 'live' or 'test'." });
            }

            try
            {
                var created = await apiKeys.CreateAsync(
                    organizationId,
                    actorUserId.Value,
                    new CreateApiKeyCommand(request.Name, request.Scopes, environment, request.ExpiresAt),
                    ct);

                return Results.Created(
                    $"/api/v1/orgs/{organizationId}/api-keys/{created.Key.Id}",
                    new CreateApiKeyResponse(ApiKeyDto.From(created.Key), created.Plaintext));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(AuthorizationConfiguration.ApiKeysManagePolicy);

        group.MapPost("/{keyId:guid}/revoke", async (
            Guid organizationId,
            Guid keyId,
            RevokeApiKeyRequest? request,
            ClaimsPrincipal principal,
            IApiKeyService apiKeys,
            IUserService users,
            CancellationToken ct) =>
        {
            try
            {
                var actorUserId = await ResolveUserIdAsync(principal, users, ct);
                var revoked = await apiKeys.RevokeAsync(organizationId, keyId, actorUserId, request?.Reason, ct);
                return Results.Ok(ApiKeyDto.From(revoked));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(AuthorizationConfiguration.ApiKeysManagePolicy);

        group.MapDelete("/{keyId:guid}", async (
            Guid organizationId, Guid keyId, IApiKeyService apiKeys, CancellationToken ct) =>
        {
            try
            {
                await apiKeys.DeleteAsync(organizationId, keyId, ct);
                return Results.NoContent();
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(AuthorizationConfiguration.ApiKeysManagePolicy);

        return endpoints;
    }

    private static IResult MapProblem(Exception exception) => exception switch
    {
        ApiKeyScopeNotAllowedException ex => Results.BadRequest(new { message = ex.Message }),
        ApiKeyValidationException ex => Results.BadRequest(new { message = ex.Message }),
        ApiKeyNotFoundException ex => Results.NotFound(new { message = ex.Message }),
        ApiKeyAlreadyUsedException ex => Results.Conflict(new { message = ex.Message }),
        ApiKeyEntitlementRequiredException ex => Results.Json(
            new { message = ex.Message }, statusCode: StatusCodes.Status403Forbidden),
        _ => throw exception,
    };

    private static bool TryParseEnvironment(string? value, out ApiKeyEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            environment = ApiKeyEnvironment.Live;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out environment);
    }

    /// <summary>
    /// Resolves the acting user id. Returns <c>null</c> for a machine (API-key) principal
    /// whose subject is not a Clerk user.
    /// </summary>
    private static async Task<Guid?> ResolveUserIdAsync(
        ClaimsPrincipal principal, IUserService users, CancellationToken ct)
    {
        if (principal.HasClaim(claim => claim.Type == ApiKeyClaimTypes.ApiKeyId))
        {
            return null;
        }

        var clerkId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(clerkId))
        {
            return null;
        }

        var profile = await users.GetByClerkIdAsync(clerkId, ct);
        return profile?.Id;
    }
}
