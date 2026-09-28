using Aveline.Api.Configurations;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Services;

namespace Aveline.Api.Modules.Commerce.Endpoints;

/// <summary>
/// The tenant dashboard's KPI reads and the reduced takings read.
/// </summary>
/// <remarks>
/// Two policies, and the split is the point:
/// <list type="bullet">
///   <item>the **full** summary, series and top-items routes take
///   <see cref="AuthorizationConfiguration.BoutiqueReportsViewPolicy"/> (<c>reports:view</c>), which
///   the grant map already gave manager, supervisor and owner;</item>
///   <item><c>…/dashboard/takings</c> takes the permission-free
///   <see cref="AuthorizationConfiguration.BoutiqueMemberPolicy"/>, because every boutique role may
///   see **two labelled figures** — money taken and billed-but-unconfirmed — and nothing else.</item>
/// </list>
/// A section is visible when its cheapest panel is readable; every richer panel inside it is hidden,
/// never 403'd. `<c>…/takings</c>` is the cheapest panel.
/// </remarks>
public static class DashboardEndpoints
{
    public const string Tag = "Dashboard";

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var orgGroup = endpoints.MapGroup("/orgs/{organizationId:guid}");

        // ── E-13: the reduced takings read, for every role ────────────────────────────────────────
        orgGroup.MapGet("/dashboard/takings", async (
            Guid organizationId,
            string? window,
            ITenantDashboardService dashboard,
            CancellationToken ct) =>
        {
            var result = await dashboard.GetTakingsAsync(
                organizationId, window ?? "30d", ct);
            return result.InvalidReason is { } reason
                ? Results.BadRequest(new { message = reason })
                : Results.Ok(result);
        })
        .WithName("getTenantTakings")
        .WithSummary("Read the boutique's takings, reduced to two labelled figures")
        .WithDescription(
            "Available to every boutique role. Carries exactly **`collected`** (verified money minus "
            + "refunds) and **`billedUnconfirmed`** (derived billed value) — no margin, no per-client "
            + "split, no register and no series, which stay behind `reports:view`. The two figures are "
            + "always labelled and never added into one earnings number.")
        .Produces<TenantTakingsDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireAuthorization(AuthorizationConfiguration.BoutiqueMemberPolicy);

        var reports = orgGroup.MapGroup("/dashboard")
            .WithTags(Tag)
            .RequireAuthorization(AuthorizationConfiguration.BoutiqueReportsViewPolicy);

        reports.MapGet("/summary", async (
            Guid organizationId,
            string? window,
            ITenantDashboardService dashboard,
            CancellationToken ct) =>
        {
            var result = await dashboard.GetSummaryAsync(
                organizationId, window ?? "30d", ct);
            return result.InvalidReason is { } reason
                ? Results.BadRequest(new { message = reason })
                : Results.Ok(result);
        })
        .WithName("getTenantDashboardSummary")
        .WithSummary("Read the tenant dashboard's KPI summary for a window")
        .WithDescription(
            "S-57. Sales, cash, customers, catalogue, team, usage and operations, each with its own "
            + "source. **Every absent number is `null`, never `0`** — a KPI that could not be computed "
            + "is not a zero — and the response carries a `dataQuality` block that names what could not "
            + "be measured. Requires `reports:view`.")
        .Produces<TenantDashboardSummaryDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        reports.MapGet("/revenue-series", async (
            Guid organizationId,
            DateTime? from,
            DateTime? to,
            string? bucket,
            ITenantDashboardService dashboard,
            CancellationToken ct) =>
        {
            var result = await dashboard.GetRevenueSeriesAsync(
                organizationId, from, to, bucket ?? "day", ct);
            return result.InvalidReason is { } reason
                ? Results.BadRequest(new { message = reason })
                : Results.Ok(result);
        })
        .WithName("getTenantRevenueSeries")
        .WithSummary("Read the boutique's revenue series over a window")
        .WithDescription(
            "S-58. Dense buckets with gross, collected and refunded figures. A bucket with no orders "
            + "carries `null`, not `0`, so the client renders a gap rather than a line through a "
            + "measurement the server never produced. The window cap is echoed in `windowCapped`. "
            + "Requires `reports:view`.")
        .Produces<TenantRevenueSeriesDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        reports.MapGet("/top-items", async (
            Guid organizationId,
            string? window,
            int? limit,
            ITenantDashboardService dashboard,
            CancellationToken ct) =>
        {
            var result = await dashboard.GetTopItemsAsync(
                organizationId, window ?? "30d", limit ?? TenantDashboardService.DefaultTopItems, ct);
            return result.InvalidReason is { } reason
                ? Results.BadRequest(new { message = reason })
                : Results.Ok(result);
        })
        .WithName("getTenantTopItems")
        .WithSummary("Read the boutique's best-selling pieces in a window")
        .WithDescription(
            "S-59. Grouped on the denormalised `ItemName`, because `OrderItem.ItemId` has no enforced "
            + "link to the catalogue: a renamed piece appears under both names rather than being "
            + "silently merged. Requires `reports:view`.")
        .Produces<TenantTopItemsDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
