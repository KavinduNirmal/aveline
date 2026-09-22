using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Notifications;
using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.Organizations.DTOs;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
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
            IOrganizationRepository organizationRepository,
            IEmailService emailService,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            if (!TryValidateInvitableRole(request.BoutiqueRole, out var roleError))
            {
                return Results.BadRequest(new { message = roleError });
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

            var validityHours = InvitationLimits.ClampValidityHours(request.ValidityHours);
            var organization = await organizationRepository.GetByIdAsync(organizationId, ct);

            CreateInvitationResponse created;
            try
            {
                created = await CreateInvitationCodeAsync(
                    organizationId,
                    request.BoutiqueRole,
                    recipientEmail,
                    validityHours,
                    profile.Id,
                    organizationService,
                    emailService,
                    configuration,
                    ct);
            }
            catch (InvitationCodeStoreUnavailableException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            // F-4: `sendSummaryToOwner` used to be silently dropped because the request record did
            // not declare it. The outcome is now always reported, including "not sent".
            var summary = await DispatchSummaryEmailAsync(
                request.SendSummaryToOwner,
                profile.Email,
                organization?.Name ?? "your boutique",
                request.BoutiqueRole,
                new[] { created.ExpiresAt },
                emailService,
                ct);

            return Results.Ok(created with
            {
                SummaryEmailRequested = summary.Requested,
                SummaryEmailStatus = summary.Status,
                SummaryEmailNote = summary.Note,
            });
        })
        .AddEndpointFilter<IdempotencyEndpointFilter>()
        .RequireAuthorization(AuthorizationConfiguration.BoutiqueTeamManagePolicy);

        // E-10. Bulk invitation creation (F-4). The tenant panel POSTs here and the route did not
        // exist; a request for N codes now mints N codes, `count` is clamped to [1,10] server-side
        // and echoed in the response, `validityHours` reaches `ExpiresAt`, and the optional summary
        // notice reports whether it was actually dispatched. The idempotency filter is the reason a
        // double-click cannot mint a duplicate batch.
        orgGroup.MapPost("/{organizationId:guid}/invitations/bulk", async (
            Guid organizationId,
            BulkCreateInvitationRequest request,
            ClaimsPrincipal principal,
            IUserService userService,
            IOrganizationService organizationService,
            IOrganizationRepository organizationRepository,
            IEmailService emailService,
            Aveline.Api.Infrastructure.RateLimiting.IRateLimiter rateLimiter,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            if (!TryValidateInvitableRole(request.BoutiqueRole, out var roleError))
            {
                return Results.BadRequest(new { message = roleError });
            }

            // `count` is the control; this limiter is the backstop against a caller that retries a
            // large batch. It is per organization, matching the scope the permission already has.
            var createLimit = int.TryParse(configuration["Invitations:CreateRateLimit"], out var parsed)
                ? parsed
                : 10;
            var allowed = await rateLimiter.TryAllowAsync(
                $"invite:bulk:{organizationId}", createLimit, TimeSpan.FromMinutes(1), ct);
            if (!allowed)
            {
                return Results.Json(
                    new { message = "Too many invitation batches. Please wait a minute and try again." },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var profile = await ResolveProfileAsync(principal, userService, ct);
            if (profile is null)
            {
                return Results.NotFound(new { message = "User record does not exist in Aveline database." });
            }

            var count = InvitationLimits.ClampBulkCount(request.Count);
            var validityHours = InvitationLimits.ClampValidityHours(request.ValidityHours);
            var organization = await organizationRepository.GetByIdAsync(organizationId, ct);

            var created = new List<CreateInvitationResponse>(count);
            try
            {
                for (var index = 0; index < count; index++)
                {
                    created.Add(await CreateInvitationCodeAsync(
                        organizationId,
                        request.BoutiqueRole,
                        recipientEmail: null,
                        validityHours,
                        profile.Id,
                        organizationService,
                        emailService,
                        configuration,
                        ct));
                }
            }
            catch (InvitationCodeStoreUnavailableException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var summary = await DispatchSummaryEmailAsync(
                request.SendSummaryToOwner,
                profile.Email,
                organization?.Name ?? "your boutique",
                request.BoutiqueRole,
                created.Select(item => item.ExpiresAt).ToList(),
                emailService,
                ct);

            return Results.Ok(new BulkCreateInvitationResponse(
                created,
                request.Count,
                created.Count,
                validityHours,
                summary.Requested,
                summary.Status,
                summary.Note));
        })
        .AddEndpointFilter<IdempotencyEndpointFilter>()
        .RequireAuthorization(AuthorizationConfiguration.BoutiqueTeamManagePolicy);

        orgGroup.MapGet("/{organizationId:guid}/invitations", async (
            Guid organizationId,
            IOrganizationService organizationService,
            CancellationToken ct) =>
        {
            var pending = await organizationService.ListPendingInvitationsAsync(organizationId, ct);
            return Results.Ok(pending.Select(i => new PendingInvitationDto(
                i.Id, i.BoutiqueRole, i.RecipientEmail, i.CreatedAt, i.ExpiresAt)));
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueTeamManagePolicy);

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
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueTeamManagePolicy);

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
                // ClerkOrgId is taken from the authenticated JWT's org claim, never from the
                // client body. A caller could otherwise bind their boutique to a Clerk org they
                // do not control, corrupting webhook-driven membership sync.
                var clerkOrgId = principal.FindFirstValue("org_id")
                                 ?? principal.FindFirstValue("orgId");

                var organization = await organizationService.CreateOrganizationAsync(
                    profile.Id,
                    request.Name,
                    string.IsNullOrWhiteSpace(clerkOrgId) ? null : clerkOrgId.Trim(),
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
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization();

        orgGroup.MapGet("/my", async (
            ClaimsPrincipal principal,
            IUserService userService,
            IOrganizationService organizationService,
            IOrganizationRepository organizationRepository,
            CancellationToken ct) =>
        {
            var profile = await ResolveProfileAsync(principal, userService, ct);
            if (profile is null)
            {
                return Results.NotFound(new { message = "User record does not exist in Aveline database." });
            }

            var memberships = await organizationService.GetUserMembershipsAsync(profile.Id, ct);
            var result = new List<object>(memberships.Count);
            foreach (var membership in memberships)
            {
                var org = await organizationRepository.GetByIdAsync(membership.OrganizationId, ct);
                result.Add(new
                {
                    membership.OrganizationId,
                    membership.UserId,
                    OrganizationName = org?.Name,
                    Slug = org?.Slug,
                    membership.BoutiqueRole,
                    membership.Status,
                });
            }
            return Results.Ok(result);
        }).RequireAuthorization();

        orgGroup.MapGet("/by-slug/{slug}", async (
            string slug,
            ClaimsPrincipal principal,
            IUserService userService,
            IOrganizationService organizationService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["slug"] = ["A boutique slug is required."],
                });
            }

            var profile = await ResolveProfileAsync(principal, userService, ct);
            if (profile is null)
            {
                return Results.NotFound(new { message = "User record does not exist in Aveline database." });
            }

            var result = await organizationService.GetOrganizationProfileBySlugAsync(
                slug.Trim().ToLowerInvariant(), profile.Id, ct);

            // Tenant isolation: resolve to a boutique only when the caller holds an active
            // membership. Unknown slugs and valid boutiques the caller does not belong to are
            // indistinguishable (404), preventing boutique existence/PII enumeration.
            var isMember = result?.Membership is not null
                && string.Equals(result.Membership.Status, MembershipStatus.Active.ToString(), StringComparison.Ordinal);
            return isMember
                ? Results.Ok(result)
                : Results.NotFound(new { message = "No boutique exists with that slug." });
        }).RequireAuthorization();

        orgGroup.MapGet("/{organizationId:guid}", async (
            Guid organizationId,
            IOrganizationService organizationService,
            CancellationToken ct) =>
        {
            var profile = await organizationService.GetOrganizationProfileAsync(organizationId, ct);
            return profile is null
                ? Results.NotFound(new { message = "Organization not found." })
                : Results.Ok(profile);
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueAccessPolicy);

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
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueTeamManagePolicy);

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
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueTeamManagePolicy);

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
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueTeamManagePolicy);

        orgGroup.MapPatch("/{organizationId:guid}", async (
            Guid organizationId,
            UpdateOrganizationSettingsRequest request,
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
                var organization = await organizationService.UpdateSettingsAsync(
                    organizationId, request, profile.Id, ct);
                return Results.Ok(OrganizationSettingsDto.From(organization));
            }
            catch (OrganizationSlugAlreadyInUseException)
            {
                return Results.Conflict(new { message = "An organization with that slug already exists." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = "Organization not found." });
            }
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueMembershipManagePolicy);

        orgGroup.MapGet("/{organizationId:guid}/settings", async (
            Guid organizationId,
            IOrganizationRepository organizationRepository,
            Aveline.Api.Modules.Billing.Domain.IEntitlementResolver entitlements,
            CancellationToken ct) =>
        {
            var organization = await organizationRepository.GetByIdAsync(organizationId, ct);
            if (organization is null)
            {
                return Results.NotFound(new { message = "Organization not found." });
            }

            var resolved = await entitlements.GetAllAsync(organizationId, null, ct);
            var entitlementList = resolved.Values
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => new
                {
                    value.Key,
                    ValueType = value.ValueType.ToString(),
                    Value = value.Number is not null
                        ? (object)value.Number
                        : value.Flag ?? (object?)value.Text,
                    value.Source,
                    value.EffectiveFrom,
                });

            return Results.Ok(new
            {
                settings = OrganizationSettingsDto.From(organization),
                entitlements = entitlementList,
            });
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueMembershipManagePolicy);

        orgGroup.MapGet("/{organizationId:guid}/members", async (
            Guid organizationId,
            string? status,
            string? role,
            string? q,
            int? page,
            int? pageSize,
            IOrganizationService organizationService,
            CancellationToken ct) =>
        {
            var members = await organizationService.ListMembersAsync(
                organizationId, status, role, q, page ?? 1, pageSize ?? 20, ct);
            return Results.Ok(members);
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueTeamManagePolicy);

        orgGroup.MapPatch("/{organizationId:guid}/members/{userId:guid}", async (
            Guid organizationId,
            Guid userId,
            ChangeMemberRoleRequest request,
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
                var member = await organizationService.ChangeMemberRoleAsync(
                    organizationId, userId, request.BoutiqueRole, profile.Id, ct);
                return Results.Ok(member);
            }
            catch (InvalidBoutiqueRoleException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (CannotChangeOwnRoleException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (CannotDemoteLastOwnerException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (OwnerRoleChangeNotPermittedException ex)
            {
                return Results.Json(
                    new { message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (MembershipNotFoundException)
            {
                return Results.NotFound(new { message = "Organization or membership not found." });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = "Organization or membership not found." });
            }
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueTeamManagePolicy);

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

    private static bool TryValidateInvitableRole(string? role, out string? error)
    {
        if (string.IsNullOrWhiteSpace(role) || !InvitableRoles.Contains(role, StringComparer.Ordinal))
        {
            error = $"'{role}' is not an invitable boutique staff role.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Mints one invitation code and builds its links. Shared by the single and bulk routes so the
    /// two cannot drift on validity, link shape or the recipient email.
    /// </summary>
    private static async Task<CreateInvitationResponse> CreateInvitationCodeAsync(
        Guid organizationId,
        string boutiqueRole,
        string? recipientEmail,
        int validityHours,
        Guid actorUserId,
        IOrganizationService organizationService,
        IEmailService emailService,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var result = await organizationService.InviteMemberAsync(
            organizationId,
            actorUserId,
            new InviteMemberRequest(
                boutiqueRole,
                RecipientEmail: recipientEmail,
                ValidFor: TimeSpan.FromHours(validityHours)),
            ct);

        var baseUrl = configuration["App:BaseUrl"];
        var link = string.IsNullOrWhiteSpace(baseUrl)
            ? $"/invite?code={result.Code}"
            : $"{baseUrl.TrimEnd('/')}/invite?code={result.Code}";

        // Mobile deep link (custom scheme) that opens the Flutter app directly
        // into the invite-code step, skipping manual entry.
        var mobileScheme = configuration["App:MobileScheme"] ?? "aveline";
        var mobileLink = $"{mobileScheme.TrimEnd('/')}://invite?code={result.Code}";

        if (recipientEmail is not null)
        {
            await emailService.SendStaffInvitationAsync(
                new StaffInvitationEmail(recipientEmail, link, boutiqueRole), ct);
        }

        return new CreateInvitationResponse(
            result.Invitation.Id,
            result.Code,
            link,
            mobileLink,
            boutiqueRole,
            result.Invitation.RecipientEmail,
            result.Invitation.ExpiresAt,
            SummaryEmailRequested: false,
            SummaryEmailStatus: InvitationSummaryEmailStatus.NotRequested,
            SummaryEmailNote: null);
    }

    /// <summary>The outcome of the optional summary notice, as the response reports it.</summary>
    private sealed record SummaryOutcome(bool Requested, string Status, string? Note);

    /// <summary>
    /// Dispatches the optional summary notice and reports what actually happened. Three outcomes are
    /// distinguished rather than collapsed into a boolean, because "not asked for" and "asked for
    /// but not sent" are different facts and the previous version silently dropped the request.
    /// </summary>
    private static async Task<SummaryOutcome> DispatchSummaryEmailAsync(
        bool requested,
        string? actorEmail,
        string organizationName,
        string boutiqueRole,
        IReadOnlyList<DateTime> expiresAt,
        IEmailService emailService,
        CancellationToken ct)
    {
        if (!requested)
        {
            return new SummaryOutcome(false, InvitationSummaryEmailStatus.NotRequested, null);
        }

        if (string.IsNullOrWhiteSpace(actorEmail))
        {
            return new SummaryOutcome(
                true,
                InvitationSummaryEmailStatus.NotSent,
                "No summary was sent: the acting user has no email address on record.");
        }

        await emailService.SendInvitationSummaryAsync(
            new InvitationSummaryEmail(
                actorEmail,
                organizationName,
                expiresAt.Count,
                boutiqueRole,
                expiresAt.Min()),
            ct);

        return new SummaryOutcome(
            true,
            InvitationSummaryEmailStatus.Dispatched,
            "Handed to the configured email sender. The repository's sender records a dispatch, not a "
            + "delivery, so this is not proof the owner received it.");
    }
}

/// <summary>
/// Request to create a boutique. <c>ClerkOrgId</c> is deliberately NOT accepted here —
/// it is derived server-side from the JWT <c>org_id</c> claim to prevent a caller from
/// claiming a Clerk organization they do not own.
/// </summary>
public record CreateOrganizationRequest(string Name, string? Slug = null);

public record AcceptInvitationRequest(string Code);
