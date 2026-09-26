using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Aveline.Api.Modules.Privacy.Services;
using Aveline.Api.Modules.Shared.Repositories;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.Extensions.Configuration;

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

            try
            {
                var created = await customers.CreateWalkInAsync(organizationId, request, actorUserId, ct);

                // A duplicate is a report, not a second client: 200 with the existing
                // id rather than 201, so the counter can say "already on file".
                return created.DuplicateOfCustomerId is null
                    ? Results.Created($"/api/v1/orgs/{organizationId}/customers/{created.CustomerId}", created)
                    : Results.Ok(created);
            }
            catch (CustomerPhoneConflictException)
            {
                // The unique `(OrganizationId, PhoneNumber)` index used to surface as a 500 through
                // the global handler. The pre-check inside the service makes the collision a typed
                // outcome on every provider, and this maps it to the same 409 the PATCH path uses.
                return Results.Conflict(new
                {
                    code = "customer-phone-conflict",
                    message = "Another client in this boutique already has that phone number.",
                });
            }
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

        // E-6. Reading one client is what makes the `Location` header on the visit receipt above
        // resolve — before this route existed the API advertised a URL it did not serve.
        group.MapGet("/{customerId:guid}", async (
            Guid organizationId,
            Guid customerId,
            ICustomerTenantService customers,
            CancellationToken ct) =>
        {
            var detail = await customers.GetDetailAsync(organizationId, customerId, ct);
            return detail is null
                ? Results.NotFound(new { message = "That client is not in this boutique." })
                : Results.Ok(detail);
        });

        // Plan §11 item 4.4. The staff consent surface lives on the tenant customer group under the
        // same policy as the rest of the client book. It is deliberately NOT the anonymous
        // /privacy/opt-out/verify route: the two must be distinguishable in the audit by ActorKind,
        // and only the customer route may carry `scope`.
        group.MapGet("/{customerId:guid}/consent", async (
            Guid organizationId,
            Guid customerId,
            ICustomerConsentService consent,
            ICustomerTenantService customers,
            CancellationToken ct) =>
        {
            // Reuse the tenant service's scoping so a consent read cannot see another boutique's
            // client: an id that is not in this organisation is a 404, indistinguishable from a
            // missing one.
            var detail = await customers.GetDetailAsync(organizationId, customerId, ct);
            if (detail is null)
            {
                return Results.NotFound(new { message = "That client is not in this boutique." });
            }

            return Results.Ok(await consent.GetTenantConsentAsync(organizationId, customerId, ct));
        });

        group.MapPost("/{customerId:guid}/consent", async (
            Guid organizationId,
            Guid customerId,
            StaffConsentUpdateRequest request,
            ClaimsPrincipal principal,
            HttpContext http,
            ICustomerConsentService consent,
            ICustomerTenantService customers,
            IUserRepository users,
            CancellationToken ct) =>
        {
            var detail = await customers.GetDetailAsync(organizationId, customerId, ct);
            if (detail is null)
            {
                return Results.NotFound(new { message = "That client is not in this boutique." });
            }

            var actorUserId = await ResolveUserIdAsync(principal, users, ct);

            try
            {
                var actor = new ConsentActor(
                    Kind: ConsentActorKinds.User,
                    Source: ConsentSources.Staff,
                    UserId: actorUserId == Guid.Empty ? null : actorUserId,
                    ActorRef: principal.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? principal.FindFirstValue("sub"),
                    IpHash: MetricDimensionHasher.HashIp(
                        http.Connection.RemoteIpAddress?.ToString(),
                        http.RequestServices.GetService<IConfiguration>()?["Telemetry:IpHashSalt"]),
                    UserAgent: http.Request.Headers.UserAgent.ToString());

                var dto = await consent.UpdateAsync(
                    organizationId, customerId, request.ConsentStatus, actor, ct);

                return Results.Ok(dto);
            }
            catch (InvalidConsentStatusException ex)
            {
                // The typed domain error the internal route also maps, deliberately a 400 rather
                // than a 500 through the global handler.
                return Results.BadRequest(new { code = ex.Code, message = ex.Message });
            }
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueCustomerManagePolicy);

        // E-9. Without a history read, a detail page shows a visit count with nothing behind it.
        group.MapGet("/{customerId:guid}/interactions", async (
            Guid organizationId,
            Guid customerId,
            int? page,
            int? pageSize,
            ICustomerTenantService customers,
            CancellationToken ct) =>
        {
            var history = await customers.GetInteractionsAsync(
                organizationId, customerId, page ?? 1, pageSize ?? 50, ct);
            return history is null
                ? Results.NotFound(new { message = "That client is not in this boutique." })
                : Results.Ok(history);
        });


        group.MapGet("/{customerId:guid}/memories", async (
            Guid organizationId,
            Guid customerId,
            ICustomerTenantService customers,
            ICustomerMemoryRepository memoryRepo,
            CancellationToken ct) =>
        {
            var detail = await customers.GetDetailAsync(organizationId, customerId, ct);
            if (detail is null)
            {
                return Results.NotFound(new { message = "That client is not in this boutique." });
            }

            var memories = await memoryRepo.ListByCustomerAsync(organizationId, customerId, ct);
            var dtos = memories.Select(m => new TenantCustomerMemoryDto(
                m.Id, m.CustomerId, m.Content, m.Category, m.Source, m.IsExplicit, m.Confidence, m.CreatedAt)).ToList();
            return Results.Ok(dtos);
        });

        group.MapGet("/{customerId:guid}/events", async (
            Guid organizationId,
            Guid customerId,
            ICustomerTenantService customers,
            ICustomerEventService events,
            CancellationToken ct) =>
        {
            var detail = await customers.GetDetailAsync(organizationId, customerId, ct);
            if (detail is null)
            {
                return Results.NotFound(new { message = "That client is not in this boutique." });
            }

            var list = await events.ListAsync(organizationId, customerId, ct);
            return Results.Ok(list);
        });

        group.MapPost("/{customerId:guid}/events", async (
            Guid organizationId,
            Guid customerId,
            AddEventRequest request,
            ICustomerTenantService customers,
            ICustomerEventService events,
            CancellationToken ct) =>
        {
            var detail = await customers.GetDetailAsync(organizationId, customerId, ct);
            if (detail is null)
            {
                return Results.NotFound(new { message = "That client is not in this boutique." });
            }

            var req = request with { OrganizationId = organizationId };
            var customerEvent = await events.AddAsync(customerId, req, ct);
            return Results.Created($"/api/v1/orgs/{organizationId}/customers/{customerId}/events/{customerEvent.Id}", customerEvent);
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueCustomerManagePolicy);

        group.MapPost("/{customerId:guid}/status", async (
            Guid organizationId,
            Guid customerId,
            ICustomerTenantService customers,
            ICustomerLoyaltyService loyalty,
            CancellationToken ct) =>
        {
            var detail = await customers.GetDetailAsync(organizationId, customerId, ct);
            if (detail is null)
            {
                return Results.NotFound(new { message = "That client is not in this boutique." });
            }

            var status = await loyalty.RecomputeAsync(organizationId, customerId, null, ct);
            return status is null
                ? Results.NotFound(new { message = "That client is not in this boutique." })
                : Results.Ok(status);
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueCustomerManagePolicy);

        // E-7. The write half needs `customers:manage`: `customers:view` is held by every role, so
        // there was nothing correct to gate an edit on.
        group.MapPatch("/{customerId:guid}", async (
            Guid organizationId,
            Guid customerId,
            UpdateCustomerRequest request,
            ICustomerTenantService customers,
            CancellationToken ct) =>
        {
            try
            {
                var updated = await customers.UpdateAsync(organizationId, customerId, request, ct);
                return updated is null
                    ? Results.NotFound(new { message = "That client is not in this boutique." })
                    : Results.Ok(updated);
            }
            catch (CustomerPhoneConflictException)
            {
                return Results.Conflict(new
                {
                    code = "customer-phone-conflict",
                    message = "Another client in this boutique already has that phone number.",
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueCustomerManagePolicy);

        // E-8. Soft delete, guarded by a business rule rather than referential integrity: an order
        // has no foreign key to a client, and `Order.CustomerName` is denormalised, so nothing is
        // orphaned — the guard stops a live sale being removed mid-transaction.
        group.MapDelete("/{customerId:guid}", async (
            Guid organizationId,
            Guid customerId,
            ICustomerTenantService customers,
            CancellationToken ct) =>
        {
            try
            {
                var deleted = await customers.DeleteAsync(organizationId, customerId, ct);
                return deleted switch
                {
                    null => Results.NotFound(new { message = "That client is not in this boutique." }),
                    // Idempotent: the deletion is the end state, so a second delete is the same
                    // answer rather than a 404 the caller could not have distinguished anyway.
                    _ => Results.NoContent(),
                };
            }
            catch (CustomerHasOpenOrdersException ex)
            {
                return Results.Conflict(new
                {
                    code = "customer-has-open-orders",
                    openOrders = ex.OpenOrders,
                    message = "This client has orders that are still live. Close or cancel them first.",
                });
            }
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueCustomerManagePolicy);

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
