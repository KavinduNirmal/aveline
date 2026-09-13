using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Modules.Admin.DTOs;
using Aveline.Api.Modules.Billing.DTOs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Shared.Services;

namespace Aveline.Api.Modules.Admin.Endpoints;

/// <summary>
/// Aveline-team organization administration: the paginated organization search
/// (FR-4.8) and per-organization entitlement overrides (FR-4.9). Neither route is
/// reachable with an API key; the permission policies only accept the bearer scheme.
/// </summary>
public static class AdminOrganizationEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    // Bounding the page keeps (page - 1) * pageSize inside int range; an unbounded page
    // wrapped to a negative OFFSET, which PostgreSQL rejects with a 500 (§3.8(a)).
    private const int MaxPage = 10_000;

    public static IEndpointRouteBuilder MapAdminOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/orgs").WithTags("Admin Organizations");

        group.MapGet("", async (
            string? q,
            bool? isActive,
            string? planTier,
            int? page,
            int? pageSize,
            IOrganizationRepository organizations,
            CancellationToken ct) =>
        {
            if (!TryParsePlanTier(planTier, out var tier))
            {
                return Results.BadRequest(new
                {
                    message = "planTier must be Seed, Bloom, Orchid, Rose or Enterprise.",
                });
            }

            var normalisedPage = page is null or < 1 ? 1 : Math.Min(page.Value, MaxPage);
            var normalisedPageSize = pageSize is null or < 1
                ? DefaultPageSize
                : Math.Min(pageSize.Value, MaxPageSize);

            var (items, total) = await organizations.SearchAsync(
                q, isActive, tier, normalisedPage, normalisedPageSize, ct);

            return Results.Ok(new PagedAdminOrganizations(
                items.Select(AdminOrganizationDto.From).ToArray(),
                normalisedPage,
                normalisedPageSize,
                total));
        }).RequireAuthorization(Permissions.AdminOrgsRead);

        group.MapPatch("/{organizationId:guid}/entitlement-overrides", async (
            Guid organizationId,
            SetEntitlementOverridesRequest? request,
            ClaimsPrincipal principal,
            IEntitlementOverrideService overrides,
            IUserService users,
            CancellationToken ct) =>
        {
            if (request?.Overrides is null || request.Overrides.Count == 0)
            {
                return Results.BadRequest(new { message = "At least one override is required." });
            }

            var actorUserId = await ResolveActorUserIdAsync(principal, users, ct);
            if (actorUserId is null)
            {
                // An unauditable entitlement change must not be written with a null actor
                // (AdminUserEndpoints returns 401 for the same situation) (§3.8(f)).
                return Results.Unauthorized();
            }

            try
            {
                var entitlements = await overrides.SetOverridesAsync(
                    organizationId, actorUserId.Value, request.Overrides, ct);

                return Results.Ok(new { organizationId, entitlements });
            }
            catch (EntitlementOverrideValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (OrganizationNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        }).RequireAuthorization(Permissions.BillingAdjust);

        return endpoints;
    }

    private static async Task<Guid?> ResolveActorUserIdAsync(
        ClaimsPrincipal principal, IUserService users, CancellationToken ct)
    {
        var clerkId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(clerkId))
        {
            return null;
        }

        var user = await users.GetByClerkIdAsync(clerkId, ct);
        return user?.Id;
    }

    private static bool TryParsePlanTier(string? value, out PlanTier? parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = null;
            return true;
        }

        if (Enum.TryParse<PlanTier>(value, ignoreCase: true, out var result) && Enum.IsDefined(result))
        {
            parsed = result;
            return true;
        }

        parsed = null;
        return false;
    }
}
