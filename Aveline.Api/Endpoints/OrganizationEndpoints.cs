using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Notifications;
using Aveline.Api.Modules.Organizations.DTOs;
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
    /// <summary>Canonical boutique staff roles an owner may invite (never the owner role).</summary>
    private static readonly string[] InvitableRoles =
    [
        Roles.BoutiqueSupervisor,
        Roles.BoutiqueManager,
        Roles.BoutiqueStaff,
    ];

    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var orgGroup = endpoints.MapGroup("/orgs");

        // Staff invitation management (boutique owners / settings:manage org scope).
        orgGroup.MapPost("/{organizationId:guid}/invitations", async (
            Guid organizationId,
            CreateInvitationRequest request,
            ClaimsPrincipal principal,
            IUserService userService,
            IOrganizationService organizationService,
            IEmailService emailService,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.BoutiqueRole)
                || !InvitableRoles.Contains(request.BoutiqueRole, StringComparer.Ordinal))
            {
                return Results.BadRequest(new { message = $"'{request.BoutiqueRole}' is not an invitable boutique staff role." });
            }

            var recipientEmail = NormalizeEmail(request.RecipientEmail);
            if (request.RecipientEmail is not null && recipientEmail is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["recipientEmail"] = ["A valid recipient email is required."],
                });
            }

            var profile = await ResolveProfileAsync(principal, userService, ct);
            if (profile is null)
            {
                return Results.NotFound(new { message = "User record does not exist in Aveline database." });
            }

            InviteMemberResult result;
            try
            {
                result = await organizationService.InviteMemberAsync(
                    organizationId,
                    profile.Id,
                    new InviteMemberRequest(request.BoutiqueRole, RecipientEmail: recipientEmail),
                    ct);
            }
            catch (InvitationCodeStoreUnavailableException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var baseUrl = configuration["App:BaseUrl"];
            var link = string.IsNullOrWhiteSpace(baseUrl)
                ? $"/invite?code={result.Code}"
                : $"{baseUrl.TrimEnd('/')}/invite?code={result.Code}";

            if (recipientEmail is not null)
            {
                await emailService.SendStaffInvitationAsync(
                    new StaffInvitationEmail(recipientEmail, link, request.BoutiqueRole), ct);
            }

            return Results.Ok(new CreateInvitationResponse(
                result.Invitation.Id,
                result.Code,
                link,
                request.BoutiqueRole,
                result.Invitation.RecipientEmail,
                result.Invitation.ExpiresAt));
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueMembershipManagePolicy);

        orgGroup.MapGet("/{organizationId:guid}/invitations", async (
            Guid organizationId,
            IOrganizationService organizationService,
            CancellationToken ct) =>
        {
            var pending = await organizationService.ListPendingInvitationsAsync(organizationId, ct);
            return Results.Ok(pending.Select(i => new PendingInvitationDto(
                i.Id, i.BoutiqueRole, i.RecipientEmail, i.CreatedAt, i.ExpiresAt)));
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueMembershipManagePolicy);

        orgGroup.MapPost("/{organizationId:guid}/invitations/{invitationId:guid}/revoke", async (
            Guid organizationId,
            Guid invitationId,
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

            try
            {
                await organizationService.RevokeInvitationAsync(organizationId, invitationId, profile.Id, ct);
                return Results.NoContent();
            }
            catch (InvitationNotFoundException)
            {
                return Results.NotFound(new { message = "The invitation was not found in this organization." });
            }
            catch (CannotRevokeAcceptedInvitationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueMembershipManagePolicy);

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

        // Membership management (boutique owners only — org-scoped settings:manage).
        orgGroup.MapPost("/{organizationId:guid}/members/{userId:guid}/suspend", async (
            Guid organizationId,
            Guid userId,
            IOrganizationService organizationService,
            CancellationToken ct) =>
        {
            try
            {
                var membership = await organizationService.SetMembershipStatusAsync(
                    organizationId, userId, MembershipStatus.Suspended, ct);
                return Results.Ok(new { membership.OrganizationId, membership.UserId, membership.BoutiqueRole, membership.Status });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = "Organization or membership not found." });
            }
            catch (CannotManageOwnerMembershipException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueMembershipManagePolicy);

        orgGroup.MapPost("/{organizationId:guid}/members/{userId:guid}/activate", async (
            Guid organizationId,
            Guid userId,
            IOrganizationService organizationService,
            CancellationToken ct) =>
        {
            try
            {
                var membership = await organizationService.SetMembershipStatusAsync(
                    organizationId, userId, MembershipStatus.Active, ct);
                return Results.Ok(new { membership.OrganizationId, membership.UserId, membership.BoutiqueRole, membership.Status });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = "Organization or membership not found." });
            }
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueMembershipManagePolicy);

        orgGroup.MapDelete("/{organizationId:guid}/members/{userId:guid}", async (
            Guid organizationId,
            Guid userId,
            IOrganizationService organizationService,
            CancellationToken ct) =>
        {
            try
            {
                await organizationService.RemoveMembershipAsync(organizationId, userId, ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = "Organization or membership not found." });
            }
            catch (CannotManageOwnerMembershipException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueMembershipManagePolicy);

        var inviteGroup = endpoints.MapGroup("/invitations");

        inviteGroup.MapPost("/accept", async (
            AcceptInvitationRequest request,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            IConfiguration configuration,
            IUserService userService,
            IOrganizationService organizationService,
            Aveline.Api.Infrastructure.RateLimiting.IRateLimiter rateLimiter,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["code"] = ["Invitation code is required."],
                });
            }

            // Brute-force guard: limit accept attempts per client IP before touching a code.
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var acceptLimit = int.TryParse(configuration["Invitations:AcceptRateLimit"], out var parsed) ? parsed : 10;
            var allowed = await rateLimiter.TryAllowAsync(
                $"invite:accept:{ip}", acceptLimit, TimeSpan.FromMinutes(1), ct);
            if (!allowed)
            {
                return Results.Json(
                    new { message = "Too many invitation attempts. Please wait a minute and try again." },
                    statusCode: StatusCodes.Status429TooManyRequests);
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

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var trimmed = email.Trim().ToLowerInvariant();
        return trimmed.Length is > 0 and <= 254 && trimmed.Contains('@') ? trimmed : null;
    }
}

public record CreateOrganizationRequest(string Name, string? Slug = null, string? ClerkOrgId = null);

public record AcceptInvitationRequest(string Code);
