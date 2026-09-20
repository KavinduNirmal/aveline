using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Microsoft.AspNetCore.Authorization;
using Aveline.Api.Modules.Analytics.DTOs;
using Aveline.Api.Modules.Analytics.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Analytics.Endpoints;

/// <summary>
/// The six business-KPI routes (S-44…S-49) under
/// <c>/api/v1/admin/statistics/business/*</c>. Bearer-only: the team-only statistics families
/// deliberately omit <c>AllowBearerOrApiKey</c>, so an API key can never reach them.
/// </summary>
/// <remarks>
/// Responses are cached through <see cref="BusinessKpiCache"/> at
/// <c>BusinessAnalytics:CacheSeconds</c> (60 s) and carry
/// <c>Cache-Control: private, max-age=…</c>, because they are authenticated admin responses
/// and must not land in a shared proxy.
/// </remarks>
public static class BusinessKpiEndpoints
{
    public const string Tag = "Admin Business Statistics";

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapBusinessKpiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            // Relative to the `v1` group that already carries the `/api/v1` prefix.
            .MapGroup("/admin/statistics/business")
            .WithTags(Tag)
            .RequireAuthorization(Permissions.AnalyticsBusinessRead);

        group.MapGet("/growth", GetGrowthAsync)
            .WithName("getBusinessGrowth")
            .WithSummary("Read platform growth per bucket")
            .WithDescription(
                "S-44. New users, new organizations and administrator access requests per bucket, "
                + "with the preceding equal-length window for a delta. Requires `analytics:business:read`.");

        group.MapGet("/active-users", GetActiveUsersAsync)
            .WithName("getBusinessActiveUsers")
            .WithSummary("Read active users per bucket and the DAU/WAU/MAU reading")
            .WithDescription(
                "S-45. An active user is an authenticated user who sent at least one request in the "
                + "window. When no request is attributed the series is `null`, never `0`. "
                + "Requires `analytics:business:read`.");

        group.MapGet("/plan-mix", GetPlanMixAsync)
            .WithName("getBusinessPlanMix")
            .WithSummary("Read the current plan-tier distribution")
            .WithDescription(
                "S-46. The tier axis comes from `Organizations.PlanTier`, which is authoritative for "
                + "every organization; billing columns are supplementary. Requires `analytics:business:read`.");

        group.MapGet("/subscriptions", GetSubscriptionTrendAsync)
            .WithName("getBusinessSubscriptionTrend")
            .WithSummary("Read active subscriptions over time, split by tier")
            .WithDescription(
                "S-47. One row per organization per UTC day, written by the daily snapshot job. The "
                + "tier comes from `Organizations.PlanTier`, so an organization with no billing row is "
                + "still counted. Buckets reconstructed from the audit ledger are marked approximate. "
                + "Requires `analytics:business:read`.");

        group.MapGet("/usage", GetUsageAsync)
            .WithName("getBusinessUsage")
            .WithSummary("Read product usage per bucket")
            .WithDescription(
                "S-48. Messages sent, agent runs, API calls, Blossom units and actual AI cost. Without "
                + "`organizationId` the API-call total includes unattributed requests (BR-6.1); with it, "
                + "`admin:orgs:read` is also required. Requires `analytics:business:read`.");

        group.MapGet("/organizations", GetOrganizationUsageAsync)
            .WithName("getBusinessOrganizationUsage")
            .WithSummary("Rank organizations by a usage measure")
            .WithDescription(
                "S-49. Ranked usage with a greatest-of last-activity reconstruction. Enumerates tenant "
                + "names, so `admin:orgs:read` is also required in addition to `analytics:business:read`.");

        return endpoints;
    }

    // ── S-44 ──────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetGrowthAsync(
        HttpContext httpContext,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? granularity,
        IBusinessKpiService service,
        BusinessKpiCache cache,
        IOptions<BusinessAnalyticsOptions> options,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var parameters = new BusinessQueryParameters { From = from, To = to, Granularity = granularity };
        if (!TryWindow(httpContext, parameters, options.Value, out var window))
        {
            return InvalidWindow(httpContext, parameters, options.Value);
        }

        return await ReadAsync<BusinessGrowthDto>(
            httpContext, cache, configuration, "growth", KeyFor(parameters, window!),
            ct => service.GetGrowthAsync(window!, KeyFor(parameters, window!), ct),
            static (dto, quality) => dto with { DataQuality = quality },
            cancellationToken);
    }

    // ── S-45 ──────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetActiveUsersAsync(
        HttpContext httpContext,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? granularity,
        IBusinessKpiService service,
        BusinessKpiCache cache,
        IOptions<BusinessAnalyticsOptions> options,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var parameters = new BusinessQueryParameters { From = from, To = to, Granularity = granularity };
        if (!TryWindow(httpContext, parameters, options.Value, out var window))
        {
            return InvalidWindow(httpContext, parameters, options.Value);
        }

        return await ReadAsync<BusinessActiveUsersDto>(
            httpContext, cache, configuration, "active-users", KeyFor(parameters, window!),
            ct => service.GetActiveUsersAsync(window!, KeyFor(parameters, window!), ct),
            static (dto, quality) => dto with { DataQuality = quality },
            cancellationToken);
    }

    // ── S-46 ──────────────────────────────────────────────────────────────────────────────

    private static Task<IResult> GetPlanMixAsync(
        HttpContext httpContext,
        IBusinessKpiService service,
        BusinessKpiCache cache,
        IConfiguration configuration,
        CancellationToken cancellationToken) =>
        ReadAsync<BusinessPlanMixDto>(
            httpContext, cache, configuration, "plan-mix", "snapshot",
            ct => service.GetPlanMixAsync("plan-mix", ct),
            static (dto, quality) => dto with { DataQuality = quality },
            cancellationToken);

    // ── S-47 ──────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetSubscriptionTrendAsync(
        HttpContext httpContext,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? granularity,
        IBusinessKpiService service,
        BusinessKpiCache cache,
        IOptions<BusinessAnalyticsOptions> options,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var parameters = new BusinessQueryParameters { From = from, To = to, Granularity = granularity };
        if (!TryWindow(httpContext, parameters, options.Value, out var window))
        {
            return InvalidWindow(httpContext, parameters, options.Value);
        }

        return await ReadAsync<BusinessSubscriptionTrendDto>(
            httpContext, cache, configuration, "subscriptions", KeyFor(parameters, window!),
            ct => service.GetSubscriptionTrendAsync(window!, KeyFor(parameters, window!), ct),
            static (dto, quality) => dto with { DataQuality = quality },
            cancellationToken);
    }

    // ── S-48 ──────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetUsageAsync(
        HttpContext httpContext,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? granularity,
        [FromQuery] Guid? organizationId,
        IBusinessKpiService service,
        BusinessKpiCache cache,
        IOptions<BusinessAnalyticsOptions> options,
        IConfiguration configuration,
        IAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var parameters = new BusinessQueryParameters
        {
            From = from,
            To = to,
            Granularity = granularity,
            OrganizationId = organizationId,
        };

        if (!TryWindow(httpContext, parameters, options.Value, out var window))
        {
            return InvalidWindow(httpContext, parameters, options.Value);
        }

        // The org drill-down enumerates a tenant's usage, so it needs the org-read permission in
        // addition to the KPI permission. Checked in the handler because the route carries no
        // organization scope.
        if (organizationId is not null
            && !(await authorization.AuthorizeAsync(httpContext.User, Permissions.AdminOrgsRead)).Succeeded)
        {
            return Results.Forbid();
        }

        return await ReadAsync<BusinessUsageDto>(
            httpContext, cache, configuration, "usage", KeyFor(parameters, window!),
            ct => service.GetUsageAsync(window!, organizationId, KeyFor(parameters, window!), ct),
            static (dto, quality) => dto with { DataQuality = quality },
            cancellationToken);
    }

    // ── S-49 ──────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetOrganizationUsageAsync(
        HttpContext httpContext,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? metric,
        [FromQuery] int? limit,
        IBusinessKpiService service,
        BusinessKpiCache cache,
        IOptions<BusinessAnalyticsOptions> options,
        IConfiguration configuration,
        IAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var parameters = new BusinessQueryParameters
        {
            From = from,
            To = to,
            Granularity = "day",
            Metric = metric,
            Limit = limit,
        };

        if (!TryWindow(httpContext, parameters, options.Value, out var window))
        {
            return InvalidWindow(httpContext, parameters, options.Value);
        }

        if (!BusinessKpiValidation.TryParseRankingMetric(metric, out var parsedMetric, out var metricError))
        {
            return Results.BadRequest(new { message = metricError });
        }

        if (!BusinessKpiValidation.TryParseLimit(limit, options.Value, out var parsedLimit, out var limitError))
        {
            return Results.BadRequest(new { message = limitError });
        }

        // The ranking enumerates tenant names, so it needs the org-read permission as well.
        if (!(await authorization.AuthorizeAsync(httpContext.User, Permissions.AdminOrgsRead)).Succeeded)
        {
            return Results.Forbid();
        }

        return await ReadAsync<BusinessOrganizationUsageDto>(
            httpContext, cache, configuration, "organizations",
            $"{KeyFor(parameters, window!)}|{parsedMetric}|{parsedLimit}",
            ct => service.GetOrganizationUsageAsync(
                window!, parsedMetric, parsedLimit, KeyFor(parameters, window!), ct),
            static (dto, quality) => dto with { DataQuality = quality },
            cancellationToken);
    }

    // ── Shared plumbing ───────────────────────────────────────────────────────────────────

    private static bool TryWindow(
        HttpContext httpContext,
        BusinessQueryParameters parameters,
        BusinessAnalyticsOptions options,
        out BusinessWindow? window) =>
        BusinessKpiValidation.TryCreateWindow(
            parameters,
            options,
            httpContext.RequestServices.GetRequiredService<TimeProvider>(),
            out window).IsValid;

    private static IResult InvalidWindow(
        HttpContext httpContext,
        BusinessQueryParameters parameters,
        BusinessAnalyticsOptions options)
    {
        // Re-run for the message so the caller is told which parameter was rejected.
        var validation = BusinessKpiValidation.TryCreateWindow(
            parameters,
            options,
            httpContext.RequestServices.GetRequiredService<TimeProvider>(),
            out _);
        return Results.BadRequest(new { message = validation.Message ?? "Invalid window." });
    }

    private static async Task<IResult> ReadAsync<TDto>(
        HttpContext httpContext,
        BusinessKpiCache cache,
        IConfiguration configuration,
        string endpoint,
        string parameters,
        Func<CancellationToken, Task<TDto>> read,
        Func<TDto, BusinessDataQualityDto, TDto> attach,
        CancellationToken cancellationToken)
        where TDto : class
    {
        httpContext.Response.Headers.CacheControl = cache.CacheControlHeader;

        var key = cache.KeyFor(endpoint, parameters);
        var payload = await cache.GetOrCreateAsync(
            key,
            cache.ConsoleTtl,
            async ct => JsonSerializer.Serialize(await read(ct), SerializerOptions),
            cancellationToken);

        var dto = JsonSerializer.Deserialize<TDto>(payload, SerializerOptions)!;
        var redis = CacheConfiguration.ResolveRedisConnectionString(configuration);
        return Results.Ok(attach(dto, cache.Describe(redis).Count == 0
            ? QualityOf(dto)
            : Merge(QualityOf(dto), cache.Describe(redis))));
    }

    private static string KeyFor(BusinessQueryParameters parameters, BusinessWindow window) =>
        $"{window.From:o}|{window.To:o}|{window.Granularity}|{parameters.OrganizationId}|{parameters.Metric}|{parameters.Limit}";

    private static BusinessDataQualityDto QualityOf<TDto>(TDto dto)
        where TDto : class =>
        dto switch
        {
            IBusinessQualityCarrier carrier => carrier.DataQuality,
            _ => BusinessDataQualityDto.Clean,
        };

    private static BusinessDataQualityDto Merge(
        BusinessDataQualityDto quality,
        IReadOnlyList<string> notes) =>
        quality with { Notes = [.. quality.Notes, .. notes] };
}
