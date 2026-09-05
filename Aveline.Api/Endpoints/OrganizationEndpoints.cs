using System.Security.Claims;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Services;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Organization and invitation endpoints backing the onboarding flows: an owner
/// creates their boutique (#51), and staff join by accepting an invitation code.
/// Account state is activated once an organization membership is established.
/// </summary>
public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var orgGroup = endpoints.MapGroup("/orgs");

        orgGroup.MapPost("", async (
            CreateOrganizationRequest request,
            ClaimsPrincipal principal,
            IUserService userService,
            IOrganizationService organizationService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["Organization name is required."],
                });
            }

            var profile = await ResolveProfileAsync(principal, userService, ct);
            if (profile is null)
            {
                return Results.NotFound(new { message = "User record does not exist in Aveline database." });
            }

            try
            {
                var organization = await organizationService.CreateOrganizationAsync(
                    profile.Id,
                    request.Name,
                    request.ClerkOrgId,
                    request.Slug,
                    ct);

                // Owner becomes active once their boutique exists.
                var account = await userService.SetAccountStateAsync(profile.ClerkId, AccountState.Active, ct);

                return Results.Ok(new
                {
                    organization = new
                    {
                        organization.Id,
                        organization.Name,
                        organization.Slug,
                        organization.ClerkOrgId,
                        organization.OwnerUserId,
                        organization.CreatedAt,
                    },
                    accountState = account?.AccountState.ToString(),
                });
            }
            catch (OrganizationSlugAlreadyInUseException)
            {
                return Results.Conflict(new { message = "An organization with that slug already exists." });
            }
        }).RequireAuthorization();

        orgGroup.MapGet("/my", async (
            ClaimsPrincipal principal,
            IUserService userService,
            IOrganizationService organizationService,
            CancellationToken ct) =>
        {
            var profile = await ResolveProfileAsync(principal, userService, ct);
            if (profile is null)
            {
                return Results.NotFound(new { message = "User record does not exist in Aveline database." });
            }

            var memberships = await organizationService.GetUserMembershipsAsync(profile.Id, ct);
            return Results.Ok(memberships.Select(m => new
            {
                m.OrganizationId,
                m.UserId,
                m.BoutiqueRole,
                m.Status,
            }));
        }).RequireAuthorization();

        var inviteGroup = endpoints.MapGroup("/invitations");

        inviteGroup.MapPost("/accept", async (
            AcceptInvitationRequest request,
            ClaimsPrincipal principal,
            IUserService userService,
            IOrganizationService organizationService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["code"] = ["Invitation code is required."],
                });
            }

            var profile = await ResolveProfileAsync(principal, userService, ct);
            if (profile is null)
            {
                return Results.NotFound(new { message = "User record does not exist in Aveline database." });
            }

            try
            {
                var accepted = await organizationService.AcceptInvitationAsync(
                    request.Code.Trim(),
                    profile.Id,
                    profile.Email,
                    ct);

                var account = await userService.SetAccountStateAsync(profile.ClerkId, AccountState.Active, ct);

                return Results.Ok(new
                {
                    accepted.OrganizationId,
                    accepted.UserId,
                    accepted.BoutiqueRole,
                    accepted.ClerkOrgId,
                    accountState = account?.AccountState.ToString(),
                });
            }
            catch (InvitationNotFoundException)
            {
                return Results.NotFound(new { message = "The invitation was not found." });
            }
            catch (InvitationNotAcceptableException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (InvitationRecipientMismatchException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (MembershipAlreadyExistsException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
        }).RequireAuthorization();

        return endpoints;
    }

    private static async Task<Aveline.Api.Modules.Shared.DTOs.UserDto?> ResolveProfileAsync(
        ClaimsPrincipal principal,
        IUserService userService,
        CancellationToken ct)
    {
        var clerkId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
        return string.IsNullOrEmpty(clerkId) ? null : await userService.GetByClerkIdAsync(clerkId, ct);
    }
}

public record CreateOrganizationRequest(string Name, string? Slug = null, string? ClerkOrgId = null);

public record AcceptInvitationRequest(string Code);
