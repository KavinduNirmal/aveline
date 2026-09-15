using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Billing.DTOs;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Modules.Billing.Endpoints;

public static class BillingStatisticsEndpoints
{
    public static IEndpointRouteBuilder MapBillingStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ---------------------------------------------------------------------------
        // Tenant Scoped Endpoints (/orgs/{organizationId}/...)
        // ---------------------------------------------------------------------------
        var orgGroup = endpoints.MapGroup("/orgs/{organizationId:guid}")
            .WithTags("Organization Billing & Usage");

        orgGroup.MapGet("/billing/burn-rate", async (
            Guid organizationId,
            [FromQuery] string? window,
            IBillingStatisticsService statistics,
            CancellationToken ct) =>
        {
            var result = await statistics.GetBurnRateAsync(organizationId, window ?? "30d", ct);
            return Results.Ok(result);
        }).RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy)
          .Produces<BurnRateResponseDto>(StatusCodes.Status200OK)
          .WithSummary("Get Blossom burn rate and projected balance exhaustion date.");

        orgGroup.MapGet("/statistics/billing/burn-rate", async (
            Guid organizationId,
            [FromQuery] string? window,
            IBillingStatisticsService statistics,
            CancellationToken ct) =>
        {
            var result = await statistics.GetBurnRateAsync(organizationId, window ?? "30d", ct);
            return Results.Ok(result);
        }).RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy)
          .Produces<BurnRateResponseDto>(StatusCodes.Status200OK)
          .WithSummary("Get Blossom burn rate and projected balance exhaustion date (alias).");

        orgGroup.MapGet("/customers/active", async (
            Guid organizationId,
            IBillingStatisticsService statistics,
            CancellationToken ct) =>
        {
            var result = await statistics.GetActiveCustomersAsync(organizationId, ct);
            return Results.Ok(result);
        }).RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy)
          .Produces<ActiveCustomersDto>(StatusCodes.Status200OK)
          .WithSummary("Get active customer count within the 90-day activity window.");

        orgGroup.MapGet("/statistics/customers/active", async (
            Guid organizationId,
            IBillingStatisticsService statistics,
            CancellationToken ct) =>
        {
            var result = await statistics.GetActiveCustomersAsync(organizationId, ct);
            return Results.Ok(result);
        }).RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy)
          .Produces<ActiveCustomersDto>(StatusCodes.Status200OK)
          .WithSummary("Get active customer count within the 90-day activity window (alias).");

        orgGroup.MapGet("/staff/seats", async (
            Guid organizationId,
            IBillingStatisticsService statistics,
            CancellationToken ct) =>
        {
            var result = await statistics.GetStaffSeatsAsync(organizationId, ct);
            return Results.Ok(result);
        }).RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy)
          .Produces<StaffSeatsDto>(StatusCodes.Status200OK)
          .WithSummary("Get staff seat allocation and active boutique role distribution.");

        orgGroup.MapGet("/statistics/staff/seats", async (
            Guid organizationId,
            IBillingStatisticsService statistics,
            CancellationToken ct) =>
        {
            var result = await statistics.GetStaffSeatsAsync(organizationId, ct);
            return Results.Ok(result);
        }).RequireAuthorization(AuthorizationConfiguration.BillingViewPolicy)
          .Produces<StaffSeatsDto>(StatusCodes.Status200OK)
          .WithSummary("Get staff seat allocation and active boutique role distribution (alias).");

        // ---------------------------------------------------------------------------
        // Admin System Statistics Endpoints (/admin/statistics/billing/...)
        // ---------------------------------------------------------------------------
        var adminGroup = endpoints.MapGroup("/admin/statistics/billing")
            .WithTags("Admin Billing Statistics")
            .RequireAuthorization(AuthorizationConfiguration.StatsSystemPolicy);

        adminGroup.MapGet("/profitability", async (
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? groupBy,
            IBillingStatisticsService statistics,
            CancellationToken ct) =>
        {
            var result = await statistics.GetProfitabilityAsync(from, to, groupBy, ct);
            return Results.Ok(result);
        }).Produces<BillingProfitabilityDto>(StatusCodes.Status200OK)
          .WithSummary("Get AI unit economics and profitability breakdown.");

        adminGroup.MapGet("/org-usage", async (
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            IBillingStatisticsService statistics,
            CancellationToken ct) =>
        {
            var result = await statistics.GetOrgUsageRankingAsync(from, to, page ?? 1, pageSize ?? 20, ct);
            return Results.Ok(result);
        }).Produces<OrgUsageRankingPageDto>(StatusCodes.Status200OK)
          .WithSummary("Get organization consumption ranking by Blossom usage.");

        adminGroup.MapGet("/adjustments", async (
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] Guid? organizationId,
            [FromQuery] string? actorUserId,
            IBillingStatisticsService statistics,
            CancellationToken ct) =>
        {
            var result = await statistics.GetAdjustmentsAsync(from, to, organizationId, actorUserId, ct);
            return Results.Ok(result);
        }).Produces<BillingAdjustmentActivityDto>(StatusCodes.Status200OK)
          .WithSummary("Get administrative Blossom adjustment and credit history.");

        adminGroup.MapGet("/plan-changes", async (
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] Guid? organizationId,
            IBillingStatisticsService statistics,
            CancellationToken ct) =>
        {
            var result = await statistics.GetPlanChangesAsync(from, to, organizationId, ct);
            return Results.Ok(result);
        }).Produces<PlanChangeHistoryDto>(StatusCodes.Status200OK)
          .WithSummary("Get plan tier upgrades, downgrades, and proration totals.");

        adminGroup.MapGet("/downgrades", async (
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] Guid? organizationId,
            IBillingStatisticsService statistics,
            CancellationToken ct) =>
        {
            var result = await statistics.GetDowngradesAsync(from, to, organizationId, ct);
            return Results.Ok(result);
        }).Produces<DowngradeStatisticsDto>(StatusCodes.Status200OK)
          .WithSummary("Get blocked downgrade attempts and violation statistics.");

        return endpoints;
    }
}
