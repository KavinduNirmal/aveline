using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
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
