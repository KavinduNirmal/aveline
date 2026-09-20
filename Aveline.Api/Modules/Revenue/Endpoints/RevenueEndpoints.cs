using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.Shared.Services;
using Aveline.Api.Modules.Revenue.DTOs;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Revenue.Services;
using Aveline.Api.Modules.Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Revenue.Endpoints;

/// <summary>
/// The administrative revenue writes (S-50). Reads arrive in R3; this file owns the three verbs that
/// put money in the journal.
/// </summary>
/// <remarks>
/// All three are idempotency-guarded through <see cref="IdempotencyEndpointFilter"/>, applied before
/// the authorization requirement in the order the Blossom routes use, and all three refuse to write
/// an entry with an unresolvable actor — an unattributable money entry is not journal-worthy.
/// </remarks>
public static class RevenueEndpoints
{
    public const string Tag = "Admin Revenue";

    public static IEndpointRouteBuilder MapRevenueWriteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/revenue").WithTags(Tag);

        group.MapPost("/ledger/verify", VerifyAsync)
            .AddEndpointFilter<IdempotencyEndpointFilter>()
            .RequireAuthorization(Permissions.RevenueManage);

        group.MapPost("/ledger/refund", RefundAsync)
            .AddEndpointFilter<IdempotencyEndpointFilter>()
            .RequireAuthorization(Permissions.RevenueRefund);

        group.MapPost("/ledger/adjust", AdjustAsync)
            .AddEndpointFilter<IdempotencyEndpointFilter>()
            .RequireAuthorization(Permissions.RevenueManage);

        return endpoints;
    }

    /// <summary>
    /// The revenue reads (S-50…S-55). Bearer-only, like the other team-only statistics families: an
    /// API key may never reach a revenue figure.
    /// </summary>
    public static IEndpointRouteBuilder MapRevenueReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/revenue")
            .WithTags(Tag)
            .RequireAuthorization(Permissions.RevenueRead);

        group.MapGet("/ledger", GetLedgerAsync)
            .WithName("getRevenueLedger")
            .WithSummary("Read the paged income ledger with its window totals")
            .WithDescription(
                "S-50. Every entry in the window, with `derivedTotal`, `verifiedTotal` and the "
                + "`unverifiedGap` between them. The gap is the surface's most important number and "
                + "is not an error. Requires `revenue:read`.")
            .Produces<IncomeLedgerPageDto>(StatusCodes.Status200OK);

        group.MapGet("/accounts", GetAccountsAsync)
            .WithName("getRevenueAccounts")
            .WithSummary("Read revenue per organization over a window")
            .WithDescription(
                "S-51. Derived, verified and net revenue per organization, with an organization whose "
                + "subscriptions have no configured price surfaced rather than shown as free. "
                + "Requires `revenue:read`.")
            .Produces<RevenueAccountsDto>(StatusCodes.Status200OK);

        var statistics = endpoints.MapGroup("/admin/statistics/revenue")
            .WithTags(Tag)
            .RequireAuthorization(Permissions.RevenueRead);

        statistics.MapGet("/overview", GetOverviewAsync)
            .WithName("getRevenueOverview")
            .WithSummary("Read MRR, ARR, ARPU and the paying-organization count")
            .WithDescription(
                "S-52. List-price **scheduled** revenue, not recognised or collected revenue. Every "
                + "measure is `null` rather than `0` when it cannot be computed: an unassigned "
                + "`PriceLkr` is not a free plan. Requires `revenue:read`.")
            .Produces<RevenueOverviewDto>(StatusCodes.Status200OK);

        statistics.MapGet("/timeseries", GetTimeseriesAsync)
            .WithName("getRevenueTimeseries")
            .WithSummary("Read derived, verified and refunded amounts per bucket")
            .WithDescription(
                "S-53. Three separate series, never merged. The bucket axis is dense and aligned to "
                + "calendar boundaries, and `isPartial` marks a bucket the window clips. "
                + "Requires `revenue:read`.")
            .Produces<RevenueTimeseriesDto>(StatusCodes.Status200OK);

        statistics.MapGet("/collections", GetCollectionsAsync)
            .WithName("getRevenueCollections")
            .WithSummary("Read the collection rate per period")
            .WithDescription(
                "S-54. Verified receipts against derived charges. `collectionRate` is `null`, never "
                + "`0`, when nothing was billed; a rate above 100% is reported rather than clipped, "
                + "because a receipt with no matching charge is a finding. Requires `revenue:read`.")
            .Produces<RevenueCollectionsDto>(StatusCodes.Status200OK);

        statistics.MapGet("/blossoms", GetBlossomSalesAsync)
            .WithName("getRevenueBlossomSales")
            .WithSummary("Read Blossom top-up pack sales")
            .WithDescription(
                "S-55. Referenced purchases only: a top-up with no payment reference writes no income "
                + "row at all, and the difference is stated rather than inferred. "
                + "Requires `revenue:read`.")
            .Produces<RevenueBlossomSalesDto>(StatusCodes.Status200OK);

        return endpoints;
    }

    // ── Verify ──────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> VerifyAsync(
        VerifyIncomeRequest request,
        ClaimsPrincipal principal,
        HttpContext http,
        IIncomeLedgerService ledger,
        IUserService users,
        IAuditService audit,
        AppDbContext db,
        CancellationToken ct)
    {
        var actor = await ResolveActorUserIdAsync(principal, users, ct);
        if (!await OrganizationExistsAsync(db, request.OrganizationId, ct))
        {
            return Results.NotFound(new { message = $"Organization '{request.OrganizationId}' was not found." });
        }

        // The verified receipt takes over the derived expectation's identity, which is why the
        // operator names the charge rather than inventing a reference.
        var derived = await db.IncomeLedgerEntries.FirstOrDefaultAsync(
            entry => entry.OrganizationId == request.OrganizationId
                && entry.SourceKind == request.SourceKind
                && entry.SourceRef == request.SourceRef
                && entry.ChargeBasis == IncomeChargeBasis.Derived
                && entry.Status == IncomeEntryStatus.Recorded,
            ct);

        try
        {
            var entry = await ledger.RecordAsync(new RecordIncomeCommand(
                request.OrganizationId,
                request.Amount,
                request.Reason,
                IncomeEntryKind.SubscriptionCharge,
                IncomeChargeBasis.Verified,
                request.SourceKind,
                request.SourceRef,
                derived?.PeriodStart,
                derived?.PeriodEnd,
                DateTime.UtcNow,
                actor,
                derived?.Id,
                IdempotencyKey(http),
                "admin.revenue.verify"), ct);

            await AuditAsync(audit, entry, ct);
            return Results.Created("/api/v1/admin/revenue/ledger", IncomeLedgerEntryDto.From(entry));
        }
        catch (Exception exception)
        {
            return MapProblem(exception);
        }
    }

    // ── Refund ──────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> RefundAsync(
        RefundIncomeRequest request,
        ClaimsPrincipal principal,
        HttpContext http,
        AppDbContext db,
        IIncomeLedgerService ledger,
        IUserService users,
        IAuditService audit,
        CancellationToken ct)
    {
        var actor = await ResolveActorUserIdAsync(principal, users, ct);

        // A refund is only legitimate against a charge that was actually collected. The check is here
        // rather than in the service because the service's contract is "append what I am told"; the
        // rule that money must first have been received is a business rule about this verb.
        var collected = await db.IncomeLedgerEntries.FirstOrDefaultAsync(
            entry => entry.OrganizationId == request.OrganizationId
                && entry.SourceKind == request.SourceKind
                && entry.SourceRef == request.SourceRef
                && entry.ChargeBasis == IncomeChargeBasis.Verified
                && entry.Status == IncomeEntryStatus.Recorded,
            ct);

        if (collected is null)
        {
            return MapProblem(new RevenueRefundNotAllowedException(
                $"no verified receipt exists for {request.SourceKind} reference '{request.SourceRef}'."));
        }

        try
        {
            var key = IdempotencyKey(http);
            var entry = await ledger.RecordAsync(new RecordIncomeCommand(
                request.OrganizationId,
                request.Amount,
                request.Reason,
                IncomeEntryKind.Refund,
                IncomeChargeBasis.Verified,
                // The refund is an operator action against a named receipt, so its own identity is
                // the idempotency key rather than the charge's reference: pointing at the charge
                // would collide with the very entry being refunded.
                IncomeSourceKind.Admin,
                string.IsNullOrWhiteSpace(key) ? $"refund-{Guid.CreateVersion7():N}" : key,
                collected.PeriodStart,
                collected.PeriodEnd,
                DateTime.UtcNow,
                actor,
                null,
                key,
                "admin.revenue.refund"), ct);

            await AuditAsync(audit, entry, ct);
            return Results.Created("/api/v1/admin/revenue/ledger", IncomeLedgerEntryDto.From(entry));
        }
        catch (Exception exception)
        {
            return MapProblem(exception);
        }
    }

    // ── Adjust ──────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> AdjustAsync(
        AdjustIncomeRequest request,
        ClaimsPrincipal principal,
        HttpContext http,
        IIncomeLedgerService ledger,
        IUserService users,
        IAuditService audit,
        CancellationToken ct)
    {
        var actor = await ResolveActorUserIdAsync(principal, users, ct);
        var key = IdempotencyKey(http);

        try
        {
            var entry = await ledger.RecordAsync(new RecordIncomeCommand(
                request.OrganizationId,
                request.Amount,
                request.Reason,
                IncomeEntryKind.Adjustment,
                // An adjustment corrects the ledger, so it is verified movement by definition and
                // never something a list price asked for.
                IncomeChargeBasis.Verified,
                IncomeSourceKind.Admin,
                string.IsNullOrWhiteSpace(request.SourceRef) ? key : request.SourceRef,
                null,
                null,
                DateTime.UtcNow,
                actor,
                request.SupersedesEntryId,
                key,
                "admin.revenue.adjust"), ct);

            await AuditAsync(audit, entry, ct);
            return Results.Created("/api/v1/admin/revenue/ledger", IncomeLedgerEntryDto.From(entry));
        }
        catch (Exception exception)
        {
            return MapProblem(exception);
        }
    }

    // ── Reads (S-50…S-55) ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The ledger register. Deliberately **uncached**: it is the surface an operator refreshes after
    /// acting on it, and the reconciliation on the page is the very thing they are checking.
    /// </summary>
    private static async Task<IResult> GetLedgerAsync(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        IRevenueStatisticsService statistics,
        HttpContext http,
        CancellationToken ct)
    {
        var window = Resolve(from, to);
        if (window is null)
        {
            return Results.BadRequest(new { message = "from must be earlier than to." });
        }

        // Uncached, but still `private`: the response carries one organization's revenue figures and
        // must not be stored by a shared cache. `max-age=0` forces revalidation rather than a stale
        // balance, which is the point of not caching it.
        http.Response.Headers.CacheControl = "private, max-age=0";

        var result = await statistics.GetLedgerAsync(
            new RevenueLedgerQuery(window, page ?? 1, pageSize ?? 50), ct);

        return result.IsValid
            ? Results.Ok(result.Value)
            : Results.BadRequest(new { message = result.Message });
    }

    private static async Task<IResult> GetAccountsAsync(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        IRevenueStatisticsService statistics,
        RevenueCache cache,
        IConfiguration configuration,
        HttpContext http,
        CancellationToken ct)
    {
        var window = Resolve(from, to);
        if (window is null)
        {
            return Results.BadRequest(new { message = "from must be earlier than to." });
        }

        return await CachedAsync(
            http, cache, configuration, "accounts", window.ToString(),
            () => statistics.GetAccountsAsync(window, ct));
    }

    private static async Task<IResult> GetOverviewAsync(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        IRevenueStatisticsService statistics,
        RevenueCache cache,
        IConfiguration configuration,
        HttpContext http,
        CancellationToken ct)
    {
        var window = Resolve(from, to);
        if (window is null)
        {
            return Results.BadRequest(new { message = "from must be earlier than to." });
        }

        return await CachedAsync(
            http, cache, configuration, "overview", window.ToString(),
            () => statistics.GetOverviewAsync(window, ct));
    }

    private static async Task<IResult> GetTimeseriesAsync(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? granularity,
        IRevenueStatisticsService statistics,
        RevenueCache cache,
        IConfiguration configuration,
        HttpContext http,
        CancellationToken ct)
    {
        var window = Resolve(from, to);
        if (window is null)
        {
            return Results.BadRequest(new { message = "from must be earlier than to." });
        }

        return await CachedAsync(
            http, cache, configuration, "timeseries", $"{window}|{granularity}",
            () => statistics.GetTimeseriesAsync(window, granularity, ct));
    }

    private static async Task<IResult> GetCollectionsAsync(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? granularity,
        IRevenueStatisticsService statistics,
        RevenueCache cache,
        IConfiguration configuration,
        HttpContext http,
        CancellationToken ct)
    {
        var window = Resolve(from, to);
        if (window is null)
        {
            return Results.BadRequest(new { message = "from must be earlier than to." });
        }

        return await CachedAsync(
            http, cache, configuration, "collections", $"{window}|{granularity}",
            () => statistics.GetCollectionsAsync(window, granularity, ct));
    }

    private static async Task<IResult> GetBlossomSalesAsync(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? granularity,
        IRevenueStatisticsService statistics,
        RevenueCache cache,
        IConfiguration configuration,
        HttpContext http,
        CancellationToken ct)
    {
        var window = Resolve(from, to);
        if (window is null)
        {
            return Results.BadRequest(new { message = "from must be earlier than to." });
        }

        return await CachedAsync(
            http, cache, configuration, "blossoms", $"{window}|{granularity}",
            () => statistics.GetBlossomSalesAsync(window, granularity, ct));
    }

    /// <summary>
    /// Resolves the window from raw query input. `null` means the request is malformed; the service
    /// applies the configured cap and names the effective limit when it rejects one.
    /// </summary>
    private static RevenueWindow? Resolve(DateTime? from, DateTime? to)
    {
        var end = to ?? DateTime.UtcNow;
        var start = from ?? end.AddDays(-RevenueWindowValidation.DefaultWindowDays);
        return start < end ? new RevenueWindow(start, end) : null;
    }

    /// <summary>
    /// Runs a statistics read, appends the cache's data-quality notes, and sets the cache header.
    /// </summary>
    /// <remarks>
    /// The quality block is re-attached on the way out rather than cached with the payload, so the
    /// per-instance note describes the **serving** instance rather than whichever replica happened
    /// to populate the key. That is the same reason <c>BusinessKpiEndpoints</c> attaches it after
    /// the read.
    /// </remarks>
    private static async Task<IResult> CachedAsync<T>(
        HttpContext http,
        RevenueCache cache,
        IConfiguration configuration,
        string endpoint,
        string parameters,
        Func<Task<RevenueResult<T>>> read)
        where T : class
    {
        http.Response.Headers.CacheControl = cache.CacheControlHeader;

        var result = await read();
        if (!result.IsValid)
        {
            return Results.BadRequest(new { message = result.Message });
        }

        var redis = CacheConfiguration.ResolveRedisConnectionString(configuration);

        // Every read DTO carries the quality block, so it is rewritten through this one place rather
        // than each handler remembering to. Typed as `object` because the arms are distinct record
        // types and `Results.Ok` does not need the concrete type back.
        object withNotes = result.Value switch
        {
            RevenueAccountsDto accounts => accounts with
            {
                DataQuality = cache.WithCacheNotes(accounts.DataQuality, redis),
            },
            RevenueOverviewDto overview => overview with
            {
                DataQuality = cache.WithCacheNotes(overview.DataQuality, redis),
            },
            RevenueTimeseriesDto timeseries => timeseries with
            {
                DataQuality = cache.WithCacheNotes(timeseries.DataQuality, redis),
            },
            RevenueCollectionsDto collections => collections with
            {
                DataQuality = cache.WithCacheNotes(collections.DataQuality, redis),
            },
            RevenueBlossomSalesDto blossoms => blossoms with
            {
                DataQuality = cache.WithCacheNotes(blossoms.DataQuality, redis),
            },
            _ => result.Value,
        };

        return Results.Ok(withNotes);
    }

    // ── Shared plumbing ─────────────────────────────────────────────────────────────────────

    private static Task AuditAsync(IAuditService audit, IncomeLedgerEntry entry, CancellationToken ct) =>
        audit.RecordAsync(new AuditEntryRequest(
            Action: RevenueAuditActions.For(entry),
            EntityType: nameof(IncomeLedgerEntry),
            EntityId: entry.Id.ToString(),
            OrganizationId: entry.OrganizationId,
            // A system-written derived charge has no actor; every administrative verb resolves one,
            // and an entry with `Guid.Empty` was already refused by the service.
            ActorKind: entry.RecordedByUserId is null ? AuditActorKind.System : AuditActorKind.User,
            ActorUserId: entry.RecordedByUserId,
            After: new { entry.Kind, entry.ChargeBasis, entry.Amount, entry.Status },
            Reason: entry.Reason), ct);

    private static string? IdempotencyKey(HttpContext http)
    {
        var value = http.Request.Headers[IdempotencyEndpointFilter.HeaderName].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static Task<bool> OrganizationExistsAsync(AppDbContext db, Guid organizationId, CancellationToken ct) =>
        db.Organizations.AnyAsync(organization => organization.Id == organizationId, ct);

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
        RevenueRefundNotAllowedException refund => Results.Conflict(new
        {
            code = refund.Code,
            message = refund.Message,
        }),
        DuplicateRevenueEntryException duplicate => Results.Conflict(new
        {
            code = duplicate.Code,
            message = duplicate.Message,
        }),
        IncomeEntryNotVoidableException notVoidable => Results.Conflict(new
        {
            code = notVoidable.Code,
            message = notVoidable.Message,
        }),
        IncomeLedgerEntryNotFoundException notFound => Results.NotFound(new { message = notFound.Message }),
        RevenueOrganizationNotFoundException orgNotFound => Results.NotFound(new { message = orgNotFound.Message }),
        RevenueValidationException validation => Results.BadRequest(new { message = validation.Message }),
        _ => throw exception,
    };
}
