using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Aveline.Api.Modules.Shared.Repositories;

namespace Aveline.Api.Endpoints;

/// <summary>
/// The tenant-facing customer surface (docs/api/README.md §C.11): the client
/// book, Home's client highlights and walk-in creation.
/// </summary>
/// <remarks>
/// Every route is org-scoped by <c>{organizationId:guid}</c> under the named
/// <see cref="AuthorizationConfiguration.BoutiqueCustomerAccessPolicy"/> policy,
/// which carries an <c>OrganizationScopeRequirement</c> for
/// <c>customers:view</c>. These routes are deliberately **not** in the
/// <c>/internal/customers</c> group: that group accepts only the internal-token
/// scheme and is consumed by the agent service, never by a staff device.
/// </remarks>
public static class CustomerTenantEndpoints
{
    public static IEndpointRouteBuilder MapCustomerTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/orgs/{organizationId:guid}/customers")
            .WithTags("Customers")
            .RequireAuthorization(AuthorizationConfiguration.BoutiqueCustomerAccessPolicy);

        group.MapGet("/highlights", async (
            Guid organizationId,
            int? limit,
            DateTime? activitySince,
            ICustomerTenantService customers,
            CancellationToken ct) =>
        {
            var highlights = await customers.GetHighlightsAsync(
                organizationId, limit ?? 25, activitySince, ct);
            return Results.Ok(highlights);
        });

        group.MapGet(string.Empty, async (
            Guid organizationId,
            string? search,
            string? level,
            int? page,
            int? pageSize,
            ICustomerTenantService customers,
            CancellationToken ct) =>
        {
            var book = await customers.GetBookAsync(
                organizationId, search, level, page ?? 1, pageSize ?? 200, ct);
            return Results.Ok(book);
        });

        group.MapPost(string.Empty, async (
            Guid organizationId,
            CreateWalkInCustomerRequest request,
            ClaimsPrincipal principal,
            ICustomerTenantService customers,
            IUserRepository users,
            CancellationToken ct) =>
        {
            var name = (request.FullName ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 200)
            {
                return Results.BadRequest(new
                {
                    message = "A name of 1 to 200 characters is required.",
                });
            }

            var actorUserId = await ResolveUserIdAsync(principal, users, ct);
            var created = await customers.CreateWalkInAsync(organizationId, request, actorUserId, ct);

            // A duplicate is a report, not a second client: 200 with the existing
            // id rather than 201, so the counter can say "already on file".
            return created.DuplicateOfCustomerId is null
                ? Results.Created($"/api/v1/orgs/{organizationId}/customers/{created.CustomerId}", created)
                : Results.Ok(created);
        }).AddEndpointFilter<IdempotencyEndpointFilter>();

        group.MapPost("/{customerId:guid}/interactions", async (
            Guid organizationId,
            Guid customerId,
            RecordCustomerInteractionRequest request,
            ClaimsPrincipal principal,
            ICustomerVisitService visits,
            IUserRepository users,
            CancellationToken ct) =>
        {
            var occurredAt = request.OccurredAtUtc;
            if (occurredAt > DateTime.UtcNow.AddMinutes(5))
            {
                return Results.BadRequest(new { message = "A visit cannot be in the future." });
            }
            if (occurredAt < DateTime.UtcNow.AddDays(-30))
            {
                return Results.BadRequest(new { message = "A visit older than 30 days is not accepted." });
            }

            var actorUserId = await ResolveUserIdAsync(principal, users, ct);
            var receipt = await visits.RecordAsync(organizationId, customerId, actorUserId, request, ct);

            return receipt is null
                ? Results.NotFound(new { message = "That client is not in this boutique." })
                : Results.Created(
                    $"/api/v1/orgs/{organizationId}/customers/{customerId}", receipt);
        }).AddEndpointFilter<IdempotencyEndpointFilter>();

        return endpoints;
    }

    private static async Task<Guid> ResolveUserIdAsync(
        ClaimsPrincipal principal, IUserRepository users, CancellationToken ct)
    {
        var clerkId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(clerkId))
        {
            return Guid.Empty;
        }

        var user = await users.GetByClerkIdAsync(clerkId, ct);
        return user?.Id ?? Guid.Empty;
    }
}
