using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Billing.DTOs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Endpoints;

/// <summary>
/// Administrative pricing endpoints (docs/api/README.md §C.1). Reads are team-only
/// (<c>PricingAdminRead</c>: Admin or Owner), because the API catalogue states
/// <c>pricing:view</c> is never available to boutique roles; write requires
/// <c>pricing:manage</c>; a past effective date additionally requires
/// <c>pricing:backdate</c>.
/// </summary>
public static class PricingEndpoints
{
    public static IEndpointRouteBuilder MapPricingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/pricing").WithTags("Admin Pricing");

        group.MapGet("/rules", async (
            string? scopeKind,
            string? provider,
            string? model,
            string? status,
            DateTime? activeAt,
            int? page,
            int? pageSize,
            IPricingService pricing,
            CancellationToken ct) =>
        {
            if (!TryParseScopeKind(scopeKind, out var parsedScope)
                || !TryParseStatus(status, out var parsedStatus))
            {
                return Results.BadRequest(new { message = "Invalid scopeKind or status filter." });
            }

            var filter = new PricingRuleFilter(parsedScope, provider, model, parsedStatus, activeAt);
            var result = await pricing.ListRulesAsync(filter, page ?? 1, pageSize ?? 50, ct);
            return Results.Ok(PricingRulePageDto.From(result));
        }).RequireAuthorization(AuthorizationConfiguration.PricingAdminReadPolicy);

        group.MapGet("/rules/{ruleId:guid}", async (
            Guid ruleId, IPricingService pricing, CancellationToken ct) =>
        {
            var rule = await pricing.GetRuleAsync(ruleId, ct);
            return rule is null ? Results.NotFound() : Results.Ok(PricingRuleDto.From(rule));
        }).RequireAuthorization(AuthorizationConfiguration.PricingAdminReadPolicy);

        group.MapPost("/rules", async (
            CreatePricingRuleRequest request,
            ClaimsPrincipal principal,
            IPricingService pricing,
            IUserService users,
            IAuthorizationService authorization,
            CancellationToken ct) =>
        {
            if (request.EffectiveFrom < DateTime.UtcNow)
            {
                var allowed = await authorization.AuthorizeAsync(principal, Permissions.PricingBackdate);
                if (!allowed.Succeeded)
                {
                    return Results.Forbid();
                }
            }

            var actorUserId = await ResolveActorUserIdAsync(principal, users, ct);

            try
            {
                var rule = await pricing.CreateRuleAsync(new CreatePricingRuleCommand(
                    request.ScopeKind,
                    request.Provider,
                    request.Model,
                    request.UnitsPerBlossom,
                    request.MinimumChargeBlossoms,
                    request.RoundingMode,
                    request.RoundingDecimals,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    request.ChangeReason,
                    actorUserId,
                    AllowBackdate: true), ct);

                return Results.Created($"/api/v1/admin/pricing/rules/{rule.Id}", PricingRuleDto.From(rule));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(Permissions.PricingManage);

        group.MapPatch("/rules/{ruleId:guid}", async (
            Guid ruleId,
            UpdatePricingRuleRequest request,
            ClaimsPrincipal principal,
            IPricingService pricing,
            IUserService users,
            CancellationToken ct) =>
        {
            try
            {
                var actorUserId = await ResolveActorUserIdAsync(principal, users, ct);
                var rule = await pricing.UpdateRuleAsync(ruleId, new UpdatePricingRuleCommand(
                    request.UnitsPerBlossom,
                    request.MinimumChargeBlossoms,
                    request.RoundingMode,
                    request.RoundingDecimals,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    request.ChangeReason,
                    actorUserId), ct);

                return Results.Ok(PricingRuleDto.From(rule));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(Permissions.PricingManage);

        group.MapPost("/rules/{ruleId:guid}/activate", async (
            Guid ruleId,
            IPricingService pricing,
            CancellationToken ct) =>
        {
            try
            {
                var rule = await pricing.ActivateRuleAsync(ruleId, effectiveFrom: null, ct);
                return Results.Ok(PricingRuleDto.From(rule));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(Permissions.PricingManage);

        group.MapPost("/rules/{ruleId:guid}/cancel", async (
            Guid ruleId,
            CancelPricingRuleRequest request,
            IPricingService pricing,
            CancellationToken ct) =>
        {
            try
            {
                var rule = await pricing.CancelRuleAsync(ruleId, request.Reason, ct);
                return Results.Ok(PricingRuleDto.From(rule));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(Permissions.PricingManage);

        // The recompute job writes compensating ledger corrections, which are Phase 2.
        // Until then the endpoint is honest about not being available yet.
        group.MapPost("/rules/{ruleId:guid}/recompute", (Guid ruleId) =>
            Results.Json(
                new { message = "Pricing recompute is not available until the ledger (Phase 2) ships." },
                statusCode: StatusCodes.Status501NotImplemented))
            .RequireAuthorization(Permissions.PricingBackdate);

        group.MapGet("/price-book", async (
            string? skuKind,
            string? planTier,
            Guid? organizationId,
            IPricingService pricing,
            CancellationToken ct) =>
        {
            if (!TryParseSkuKind(skuKind, out var parsedSku)
                || !TryParsePlanTier(planTier, out var parsedTier))
            {
                return Results.BadRequest(new { message = "Invalid skuKind or planTier filter." });
            }

            var entries = await pricing.ListPriceEntriesAsync(parsedSku, parsedTier, organizationId, ct);
            return Results.Ok(entries.Select(PricingPriceEntryDto.From));
        }).RequireAuthorization(AuthorizationConfiguration.PricingAdminReadPolicy);

        group.MapGet("/price-book/{entryId:guid}", async (
            Guid entryId,
            Guid? organizationId,
            IPricingService pricing,
            CancellationToken ct) =>
        {
            var entry = await pricing.GetPriceEntryAsync(entryId, organizationId, ct);
            return entry is null ? Results.NotFound() : Results.Ok(PricingPriceEntryDto.From(entry));
        }).RequireAuthorization(AuthorizationConfiguration.PricingAdminReadPolicy);

        group.MapPost("/price-book", async (
            CreatePriceEntryRequest request,
            ClaimsPrincipal principal,
            IPricingService pricing,
            IUserService users,
            CancellationToken ct) =>
        {
            try
            {
                var actorUserId = await ResolveActorUserIdAsync(principal, users, ct);
                var entry = await pricing.CreatePriceEntryAsync(new CreatePriceEntryCommand(
                    request.PlanTier,
                    request.OrganizationId,
                    request.SkuKind,
                    request.SkuCode,
                    request.BlossomQuantity,
                    request.PriceLkr,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    request.ChangeReason,
                    actorUserId), ct);

                return Results.Created($"/api/v1/admin/pricing/price-book/{entry.Id}", PricingPriceEntryDto.From(entry));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(Permissions.PricingManage);

        group.MapPatch("/price-book/{entryId:guid}", async (
            Guid entryId,
            UpdatePriceEntryRequest request,
            ClaimsPrincipal principal,
            IPricingService pricing,
            IUserService users,
            CancellationToken ct) =>
        {
            try
            {
                var actorUserId = await ResolveActorUserIdAsync(principal, users, ct);
                var entry = await pricing.UpdatePriceEntryAsync(entryId, new UpdatePriceEntryCommand(
                    request.BlossomQuantity,
                    request.PriceLkr,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    request.ChangeReason,
                    request.Status,
                    actorUserId), ct);

                return Results.Ok(PricingPriceEntryDto.From(entry));
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(Permissions.PricingManage);

        group.MapDelete("/price-book/{entryId:guid}", async (
            Guid entryId,
            ClaimsPrincipal principal,
            IPricingService pricing,
            IUserService users,
            CancellationToken ct) =>
        {
            try
            {
                var actorUserId = await ResolveActorUserIdAsync(principal, users, ct);
                var entry = await pricing.UpdatePriceEntryAsync(entryId, new UpdatePriceEntryCommand(
                    null, null, null, null,
                    "Removed by administrator.",
                    BlossomRuleStatus.Cancelled,
                    actorUserId), ct);

                return Results.NoContent();
            }
            catch (Exception exception)
            {
                return MapProblem(exception);
            }
        }).RequireAuthorization(Permissions.PricingManage);

        return endpoints;
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
        PricingRuleNotFoundException => Results.NotFound(new { message = exception.Message }),
        PricingPriceEntryNotFoundException => Results.NotFound(new { message = exception.Message }),
        PricingBackdateForbiddenException => Results.Forbid(),
        PricingRuleImmutableException => Results.Conflict(new
        {
            code = "rule-immutable",
            message = exception.Message,
        }),
        PricingRuleOverlapException => Results.Conflict(new
        {
            code = "rule-overlap",
            message = exception.Message,
        }),
        PricingValidationException => Results.BadRequest(new { message = exception.Message }),
        DbUpdateException => Results.Conflict(new
        {
            code = "rule-overlap",
            message = "The effective window overlaps an existing rule for the same scope.",
        }),
        _ => throw exception,
    };

    private static bool TryParseScopeKind(string? value, out BlossomRuleScopeKind? parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = null;
            return true;
        }

        if (Enum.TryParse<BlossomRuleScopeKind>(value, ignoreCase: true, out var result))
        {
            parsed = result;
            return true;
        }

        parsed = null;
        return false;
    }

    private static bool TryParseStatus(string? value, out BlossomRuleStatus? parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = null;
            return true;
        }

        if (Enum.TryParse<BlossomRuleStatus>(value, ignoreCase: true, out var result))
        {
            parsed = result;
            return true;
        }

        parsed = null;
        return false;
    }

    private static bool TryParseSkuKind(string? value, out BlossomSkuKind? parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = null;
            return true;
        }

        if (Enum.TryParse<BlossomSkuKind>(value, ignoreCase: true, out var result))
        {
            parsed = result;
            return true;
        }

        parsed = null;
        return false;
    }

    private static bool TryParsePlanTier(string? value, out PlanTier? parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = null;
            return true;
        }

        if (Enum.TryParse<PlanTier>(value, ignoreCase: true, out var result))
        {
            parsed = result;
            return true;
        }

        parsed = null;
        return false;
    }
}
