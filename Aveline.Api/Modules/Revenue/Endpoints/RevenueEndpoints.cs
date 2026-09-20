using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.Revenue.DTOs;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Revenue.Services;
using Aveline.Api.Modules.Shared.Services;
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
