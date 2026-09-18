using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Billing.DTOs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Endpoints;

/// <summary>
/// Organisation-facing and administrator Blossom account endpoints
/// (docs/api/README.md §C.2). Reads require <c>billing:view</c>; top-ups require
/// <c>billing:manage</c>; administrative adjustments require <c>billing:adjust</c>.
/// </summary>
public static class BlossomEndpoints
{
    private const int MaxWindowDays = 92;

    public static IEndpointRouteBuilder MapBlossomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapOrgEndpoints(endpoints);
        MapAdminEndpoints(endpoints);
        return endpoints;
    }

    private static void MapOrgEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/orgs/{organizationId:guid}/blossoms").WithTags("Blossoms");

        group.MapGet("/balance", async (
            Guid organizationId, IBlossomService blossoms, IConfiguration configuration,
            CancellationToken ct) =>
        {
            try
            {
                var balance = await blossoms.GetBalanceAsync(organizationId, ct);
                var threshold = configuration.GetValue("Billing:LowBalanceThresholdPercent", 20m);
                return Results.Ok(BlossomBalanceDto.From(balance, threshold));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueBillingSelfViewPolicy);

        group.MapGet("/usage", async (
            Guid organizationId, DateTime? from, DateTime? to, string? groupBy,
            IBlossomService blossoms, CancellationToken ct) =>
        {
            var window = ResolveWindow(from, to);
            if (window is null)
            {
                return Results.BadRequest(new { message = $"The window must be at most {MaxWindowDays} days." });
            }

            try
            {
                var usage = await blossoms.GetUsageAsync(
                    organizationId, window.Value.From, window.Value.To, groupBy ?? "day", ct);
                return Results.Ok(usage);
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy);

        group.MapGet("/statement", (
            Guid organizationId, DateTime? from, DateTime? to, string? entryType,
            int? page, int? pageSize, IBlossomService blossoms, CancellationToken ct) =>
            StatementAsync(organizationId, from, to, entryType, page, pageSize, blossoms, ct))
            .RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy);

        group.MapPost("/top-ups", async (
            Guid organizationId,
            TopUpRequest request,
            ClaimsPrincipal principal,
            HttpContext http,
            IBlossomService blossoms,
            IPricingService pricing,
            IUserService users,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            try
            {
                var priceEntry = (await pricing.ListPriceEntriesAsync(
                        BlossomSkuKind.TopUpPack, planTier: null, organizationId: null, ct))
                    .FirstOrDefault(entry => entry.SkuCode == request.SkuCode
                        && entry.Status == BlossomRuleStatus.Active);

                if (priceEntry is null)
                {
                    return Results.BadRequest(new { code = "unknown-sku", message = "Unknown top-up SKU." });
                }

                if (request.BlossomQuantity is { } requested
                    && requested != priceEntry.BlossomQuantity)
                {
                    return Results.BadRequest(new { message = "BlossomQuantity does not match the SKU." });
                }

                var allowCrossPeriod = configuration.GetValue("Billing:AllowCrossPeriodTopUps", false);
                var now = DateTime.UtcNow;
                var periodEnd = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                var expiresAt = allowCrossPeriod ? (DateTime?)null : periodEnd;

                var actorUserId = await ResolveActorUserIdAsync(principal, users, ct);
                var entry = await blossoms.CreditAsync(new CreditBlossomsCommand(
                    organizationId,
                    priceEntry.BlossomQuantity,
                    $"Top-up purchase {request.SkuCode} ({priceEntry.BlossomQuantity} Blossoms).",
                    expiresAt,
                    BlossomSourceKind.PaymentProvider,
                    request.PaymentReference,
                    actorUserId,
                    IdempotencyKey(http),
                    "org.blossoms.top-up",
                    BlossomLedgerEntryType.TopUpGrant), ct);

                return Results.Created(
                    $"/api/v1/orgs/{organizationId}/blossoms/statement", BlossomLedgerEntryDto.From(entry));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        })
        .AddEndpointFilter<IdempotencyEndpointFilter>()
        .RequireAuthorization(AuthorizationConfiguration.BillingManagePolicy);
    }

    private static void MapAdminEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/orgs/{organizationId:guid}/blossoms").WithTags("Admin Blossoms");

        group.MapPost("/credit", async (
            Guid organizationId,
            CreditBlossomsRequest request,
            ClaimsPrincipal principal,
            HttpContext http,
            IBlossomService blossoms,
            IUserService users,
            CancellationToken ct) =>
        {
            try
            {
                var actorUserId = await ResolveActorUserIdAsync(principal, users, ct);
                var entry = await blossoms.CreditAsync(new CreditBlossomsCommand(
                    organizationId,
                    request.Amount,
                    request.Reason,
                    request.ExpiresAt,
                    request.SourceKind ?? BlossomSourceKind.Admin,
                    request.SourceRef,
                    actorUserId,
                    IdempotencyKey(http),
                    "admin.blossoms.credit"), ct);

                return Results.Created(
                    $"/api/v1/admin/orgs/{organizationId}/blossoms/statement", BlossomLedgerEntryDto.From(entry));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        })
        .AddEndpointFilter<IdempotencyEndpointFilter>()
        .RequireAuthorization(Permissions.BillingAdjust);

        group.MapPost("/debit", async (
            Guid organizationId,
            DebitBlossomsRequest request,
            ClaimsPrincipal principal,
            HttpContext http,
            IBlossomService blossoms,
            IUserService users,
            CancellationToken ct) =>
        {
            try
            {
                var actorUserId = await ResolveActorUserIdAsync(principal, users, ct);
                var entry = await blossoms.DebitAsync(new DebitBlossomsCommand(
                    organizationId,
                    request.Amount,
                    request.Reason,
                    request.AllowNegative,
                    actorUserId,
                    IdempotencyKey(http),
                    "admin.blossoms.debit"), ct);

                return Results.Created(
                    $"/api/v1/admin/orgs/{organizationId}/blossoms/statement", BlossomLedgerEntryDto.From(entry));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        })
        .AddEndpointFilter<IdempotencyEndpointFilter>()
        .RequireAuthorization(Permissions.BillingAdjust);

        group.MapPost("/revoke", async (
            Guid organizationId,
            RevokeBlossomsRequest request,
            ClaimsPrincipal principal,
            HttpContext http,
            IBlossomService blossoms,
            IUserService users,
            CancellationToken ct) =>
        {
            try
            {
                var actorUserId = await ResolveActorUserIdAsync(principal, users, ct);
                var entry = await blossoms.RevokeAsync(new RevokeBlossomsCommand(
                    organizationId,
                    request.LedgerEntryId,
                    request.Reason,
                    actorUserId,
                    IdempotencyKey(http),
                    "admin.blossoms.revoke"), ct);

                return Results.Created(
                    $"/api/v1/admin/orgs/{organizationId}/blossoms/statement", BlossomLedgerEntryDto.From(entry));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        })
        .AddEndpointFilter<IdempotencyEndpointFilter>()
        .RequireAuthorization(Permissions.BillingAdjust);

        group.MapGet("/statement", (
            Guid organizationId, DateTime? from, DateTime? to, string? entryType,
            int? page, int? pageSize, IBlossomService blossoms, CancellationToken ct) =>
            StatementAsync(organizationId, from, to, entryType, page, pageSize, blossoms, ct))
            .RequireAuthorization(Permissions.BillingAdjust);
    }

    private static async Task<IResult> StatementAsync(
        Guid organizationId, DateTime? from, DateTime? to, string? entryType,
        int? page, int? pageSize, IBlossomService blossoms, CancellationToken ct)
    {
        var window = ResolveWindow(from, to);
        if (window is null)
        {
            return Results.BadRequest(new { message = $"The window must be at most {MaxWindowDays} days." });
        }

        try
        {
            var statement = await blossoms.GetStatementAsync(
                organizationId, window.Value.From, window.Value.To, entryType,
                page ?? 1, pageSize ?? 50, ct);
            return Results.Ok(statement);
        }
        catch (Exception exception)
        {
            return MapProblem(exception);
        }
    }

    private static (DateTime From, DateTime To)? ResolveWindow(DateTime? from, DateTime? to)
    {
        var end = to ?? DateTime.UtcNow;
        var start = from ?? end.AddDays(-30);

        if (end <= start || (end - start).TotalDays > MaxWindowDays)
        {
            return null;
        }

        return (start, end);
    }

    private static string? IdempotencyKey(HttpContext http)
    {
        var value = http.Request.Headers[IdempotencyEndpointFilter.HeaderName].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static async Task<Guid> ResolveActorUserIdAsync(
        ClaimsPrincipal principal, IUserService users, CancellationToken ct)
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

    private static IResult MapProblem(Exception exception) => exception switch
    {
        BlossomLedgerEntryNotFoundException notFound => Results.NotFound(new { message = notFound.Message }),
        BlossomOrganizationNotFoundException orgNotFound => Results.NotFound(new { message = orgNotFound.Message }),
        PricingRuleNotFoundException => Results.NotFound(),
        InsufficientBalanceException insufficient => Results.Conflict(new
        {
            code = "insufficient-balance",
            message = insufficient.Message,
            available = insufficient.Available,
            requested = insufficient.Requested,
        }),
        GrantNotRevocableException notRevocable => Results.Conflict(new
        {
            code = "grant-not-revocable",
            message = notRevocable.Message,
            availableToRevoke = notRevocable.AvailableToRevoke,
        }),
        PeriodClosedException periodClosed => Results.Conflict(new
        {
            code = "period-closed",
            message = periodClosed.Message,
        }),
        ConcurrentModificationException concurrent => Results.Conflict(new
        {
            code = "concurrent-modification",
            message = concurrent.Message,
        }),
        BlossomValidationException validation => Results.BadRequest(new { message = validation.Message }),
        DbUpdateException => Results.Conflict(new
        {
            code = "idempotency-key-reuse",
            message = "The Idempotency-Key was already used with a different request body.",
        }),
        _ => throw exception,
    };
}
