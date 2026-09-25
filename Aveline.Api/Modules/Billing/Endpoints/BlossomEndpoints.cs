using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.DTOs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Revenue.Services;
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
    // The effective statement window cap lives on the service (`MaxStatementWindowDays`, 400), so
    // the endpoint and the response cannot disagree about it.
    private const int MaxWindowDays = BlossomService.MaxStatementWindowDays;

    /// <summary>
    /// What a boutique sees in place of the operator-side consumption reason. The stored reason
    /// names the provider and model that produced the charge ("AI workflow on gpt-4o"), which is
    /// agent-internals detail a tenant does not read; their usage unit is the Blossom.
    /// </summary>
    private const string BoutiqueConsumptionReason = "Blossom consumption.";

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
            string? kind, BlossomSourceKind? sourceKind, string? q,
            decimal? minAmount, decimal? maxAmount, int? page, int? pageSize,
            IBlossomService blossoms, CancellationToken ct) =>
            StatementAsync(
                organizationId, from, to, entryType, kind, null, sourceKind, q,
                minAmount, maxAmount, page, pageSize, includeOperatorDetail: false,
                blossoms, ct))
            .RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy);

        group.MapPost("/top-ups", async (
            Guid organizationId,
            TopUpRequest request,
            ClaimsPrincipal principal,
            HttpContext http,
            IBlossomService blossoms,
            IPricingService pricing,
            IUserService users,
            AppDbContext db,
            IIncomeLedgerService revenue,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            try
            {
                var now = DateTime.UtcNow;
                var priceEntry = PriceBookSelection.SelectActiveSku(
                    await pricing.ListPriceEntriesAsync(
                        BlossomSkuKind.TopUpPack, planTier: null, organizationId: null, ct),
                    request.SkuCode,
                    now);

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
                var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var periodEnd = periodStart.AddMonths(1);
                var expiresAt = allowCrossPeriod ? (DateTime?)null : periodEnd;

                var actorUserId = await ResolveActorUserIdAsync(principal, users, ct);

                // The grant and its revenue row are one unit of work, so a failure cannot leave the
                // Blossoms granted without the charge that explains them (Revenue Ledger R2).
                var ownsTransaction = db.Database.IsRelational();
                var transaction = ownsTransaction
                    ? await db.Database.BeginTransactionAsync(ct)
                    : null;

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

                await RecordTopUpRevenueAsync(
                    revenue, request, organizationId, priceEntry, periodStart, periodEnd,
                    actorUserId, ct);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(ct);
                    await transaction.DisposeAsync();
                }

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

        // E-11. The catalogue the top-up dialog offers, on the **same** policy as the purchase it
        // feeds: a caller who may buy a pack is exactly a caller who may see what is for sale, and
        // nobody else needs either. The selection is deliberately the purchase route's own
        // (`PriceBookSelection.SelectActiveSku` with `BlossomSkuKind.TopUpPack, planTier: null,
        // organizationId: null`), so a SKU shown here cannot be rejected there and a SKU accepted
        // there cannot be missing here (B-4 / TD9). Effective-dating is applied on both sides: a
        // re-priced SKU appears once, at the price its effective window carries (G7).
        group.MapGet("/top-up-packs", async (
            Guid organizationId, IPricingService pricing, CancellationToken ct) =>
        {
            var entries = await pricing.ListPriceEntriesAsync(
                BlossomSkuKind.TopUpPack, planTier: null, organizationId: null, ct);

            var now = DateTime.UtcNow;
            var packs = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.SkuCode))
                .Select(entry => entry.SkuCode!)
                .Distinct(StringComparer.Ordinal)
                .Select(skuCode => PriceBookSelection.SelectActiveSku(entries, skuCode, now))
                .OfType<BlossomPriceEntry>()
                .OrderBy(entry => entry.BlossomQuantity)
                .Select(entry => new TopUpPackDto(
                    entry.SkuCode!, entry.BlossomQuantity, entry.PriceLkr, PriceBookCurrency))
                .ToList();

            return Results.Ok(packs);
        })
        .WithName("getBlossomTopUpPacks")
        .WithSummary("List the Blossom top-up packs this boutique may purchase")
        .WithDescription(
            "The active top-up packs from the price book, ordered by size, with their LKR list "
            + "price. Requires `billing:manage`, the same permission the purchase needs. No payment "
            + "provider is connected, so a purchase records a grant rather than a charge.")
        .Produces<IReadOnlyList<TopUpPackDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireAuthorization(AuthorizationConfiguration.BillingManagePolicy);
    }

    /// <summary>
    /// The currency the price book is denominated in. `BlossomPriceEntry.PriceLkr` stores LKR and
    /// there is no currency column to read, so this is stated once rather than inferred per row.
    /// </summary>
    private const string PriceBookCurrency = "LKR";

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
            BlossomSourceKind? sourceKind, string? q, decimal? minAmount, decimal? maxAmount,
            int? page, int? pageSize, IBlossomService blossoms, CancellationToken ct) =>
            StatementAsync(
                organizationId, from, to, entryType, null, null, sourceKind, q,
                minAmount, maxAmount, page, pageSize, includeOperatorDetail: true,
                blossoms, ct))
            .RequireAuthorization(Permissions.BillingAdjust);
    }

    /// <summary>
    /// The statement, for both the org-scoped and the admin route (S-3).
    /// </summary>
    /// <remarks>
    /// Filters and paging are applied **server-side** (Revenue Ledger R4). Every rejection names the
    /// effective limit rather than silently clamping: a caller who asked for 500 rows and got 200
    /// would page on a false assumption.
    /// </remarks>
    private static async Task<IResult> StatementAsync(
        Guid organizationId, DateTime? from, DateTime? to, string? entryType, string? kind,
        BlossomLedgerEntryType? parsedEntryType, BlossomSourceKind? sourceKind, string? q,
        decimal? minAmount, decimal? maxAmount, int? page, int? pageSize,
        bool includeOperatorDetail, IBlossomService blossoms, CancellationToken ct)
    {
        if (!TryParseEntryType(entryType, out var entryFilter, out var entryError))
        {
            return Results.BadRequest(new { message = entryError });
        }

        if (!TryParseKind(kind, out var kindFilter, out var kindError))
        {
            return Results.BadRequest(new { message = kindError });
        }

        if (pageSize is { } requested && (requested < 1 || requested > BlossomService.MaxStatementPageSize))
        {
            return Results.BadRequest(new
            {
                message = $"pageSize must be between 1 and {BlossomService.MaxStatementPageSize}.",
            });
        }

        if (page is { } requestedPage && requestedPage < 1)
        {
            return Results.BadRequest(new { message = "page must be at least 1." });
        }

        if (minAmount is { } min && maxAmount is { } max && min > max)
        {
            return Results.BadRequest(new { message = "min must not exceed max." });
        }

        var window = ResolveWindow(from, to);
        if (window is null)
        {
            return Results.BadRequest(new
            {
                message = $"The window must be at most {BlossomService.MaxStatementWindowDays} days.",
            });
        }

        try
        {
            var statement = await blossoms.GetStatementAsync(
                organizationId, window.Value.From, window.Value.To, kindFilter, entryFilter,
                sourceKind, q, minAmount, maxAmount,
                page ?? 1, pageSize ?? BlossomService.DefaultStatementPageSize, ct);
            return Results.Ok(includeOperatorDetail ? statement : ForBoutique(statement));
        }
        catch (Exception exception)
        {
            return MapProblem(exception);
        }
    }

    /// <summary>
    /// Strips operator-side detail from a statement before it reaches a boutique.
    /// </summary>
    /// <remarks>
    /// A grant row keeps its own reason (the tenant needs to know why Blossoms were added). A
    /// consumption row does not: its stored reason names the provider and model, this response also
    /// carries the raw normalized units and provider cost behind the charge, and its source
    /// reference is the agent workflow id. A boutique reads its usage in Blossoms, so the reason
    /// becomes <see cref="BoutiqueConsumptionReason"/> and the agent-internals fields are nulled.
    /// The team-only admin route keeps the full detail.
    /// </remarks>
    private static BlossomStatement ForBoutique(BlossomStatement statement) => statement with
    {
        Items = statement.Items
            .Select(item => item.Kind == "Consumption"
                ? item with
                {
                    Reason = BoutiqueConsumptionReason,
                    SourceRef = null,
                    Provider = null,
                    Model = null,
                    NormalizedUnits = null,
                    ActualCostUsd = null,
                }
                : item)
            .ToArray(),
    };

    /// <summary>
    /// Parses the `entryType` filter. An unrecognised value is rejected rather than ignored: silently
    /// dropping it would return the whole window under a request that asked for a subset.
    /// </summary>
    private static bool TryParseEntryType(
        string? value, out BlossomLedgerEntryType? entryType, out string? error)
    {
        entryType = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Enum.TryParse<BlossomLedgerEntryType>(value, ignoreCase: true, out var parsed))
        {
            error = $"entryType must be one of {string.Join(", ", Enum.GetNames<BlossomLedgerEntryType>())}.";
            return false;
        }

        entryType = parsed;
        return true;
    }

    private static bool TryParseKind(string? value, out string? kind, out string? error)
    {
        kind = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        error = null;

        if (kind is null or "all" or "entitlement" or "consumption")
        {
            return true;
        }

        error = "kind must be one of all, entitlement or consumption.";
        return false;
    }

    /// <summary>
    /// Records the revenue a top-up represents — and **only** when a payment reference is present.
    /// </summary>
    /// <remarks>
    /// There is no payment-provider client in this repository, so a top-up grant is not a charge:
    /// `docs/api/README.md` §C.2 records the position plainly. What a top-up does give us is the
    /// provider session reference the caller supplied, which is the operator's evidence that money
    /// changed hands. Without it, a granted pack writes **no** income row at all, because booking a
    /// free grant as revenue would invent it.
    ///
    /// The entry is <see cref="IncomeChargeBasis.Derived"/> rather than `Verified`: a reference is
    /// evidence of a session, not proof of settlement, and only an operator confirming receipt makes
    /// the money real.
    /// </remarks>
    private static async Task RecordTopUpRevenueAsync(
        IIncomeLedgerService revenue,
        TopUpRequest request,
        Guid organizationId,
        BlossomPriceEntry priceEntry,
        DateTime periodStart,
        DateTime periodEnd,
        Guid? actorUserId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PaymentReference))
        {
            return;
        }

        await revenue.RecordAsync(new RecordIncomeCommand(
            organizationId,
            priceEntry.PriceLkr,
            $"Top-up purchase {request.SkuCode} ({priceEntry.BlossomQuantity} Blossoms).",
            IncomeEntryKind.TopUpPurchase,
            IncomeChargeBasis.Derived,
            IncomeSourceKind.BlossomTopUp,
            // The provider reference is the dedup identity, so a retried top-up cannot double-book
            // even if its idempotency lease has expired.
            request.PaymentReference.Trim(),
            periodStart,
            periodEnd,
            DateTime.UtcNow,
            actorUserId), ct);
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
