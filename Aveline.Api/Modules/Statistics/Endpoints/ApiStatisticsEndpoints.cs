using Aveline.Api.Configurations;
using Aveline.Api.Modules.Statistics.Domain;
using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Services;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Statistics.Endpoints;

/// <summary>
/// Organisation-facing API consumption statistics (docs/api/README.md §C.7) and the
/// team-only admin subset. Invalid status/statusClass/groupBy/window values return 400 with
/// <c>{ message }</c> rather than a 500; percentiles below the sample floor are <c>null</c>
/// with a reason.
/// </summary>
public static class ApiStatisticsEndpoints
{
    public static IEndpointRouteBuilder MapApiStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapOrgEndpoints(endpoints);
        MapAdminEndpoints(endpoints);
        return endpoints;
    }

    private static void MapOrgEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/orgs/{organizationId:guid}/statistics/api")
            .WithTags("API Statistics")
            .RequireAuthorization(AuthorizationConfiguration.StatsViewPolicy);

        group.MapGet("/requests", async (
            Guid organizationId, DateTime? from, DateTime? to, string? routeTemplate,
            string? httpMethod, string? status, string? statusClass, Guid? apiKeyId, Guid? userId,
            int? page, int? pageSize, string? groupBy, IApiStatisticsService statistics,
            IOptions<TelemetryOptions> options, CancellationToken ct) =>
        {
            var (filter, error) = Build(organizationId, from, to, routeTemplate, httpMethod, status,
                statusClass, apiKeyId, userId, page, pageSize, groupBy, options.Value);
            return error ?? Results.Ok(await statistics.GetRequestsAsync(filter!, ct));
        });

        group.MapGet("/errors", async (
            Guid organizationId, DateTime? from, DateTime? to, string? routeTemplate,
            string? httpMethod, string? status, string? statusClass, Guid? apiKeyId, Guid? userId,
            int? page, int? pageSize, string? groupBy, IApiStatisticsService statistics,
            IOptions<TelemetryOptions> options, CancellationToken ct) =>
        {
            var (filter, error) = Build(organizationId, from, to, routeTemplate, httpMethod, status,
                statusClass, apiKeyId, userId, page, pageSize, groupBy, options.Value);
            return error ?? Results.Ok(await statistics.GetErrorsAsync(filter!, ct));
        });

        group.MapGet("/latency", async (
            Guid organizationId, DateTime? from, DateTime? to, string? routeTemplate,
            string? httpMethod, string? status, string? statusClass, Guid? apiKeyId, Guid? userId,
            int? page, int? pageSize, string? groupBy, IApiStatisticsService statistics,
            IOptions<TelemetryOptions> options, CancellationToken ct) =>
        {
            var (filter, error) = Build(organizationId, from, to, routeTemplate, httpMethod, status,
                statusClass, apiKeyId, userId, page, pageSize, groupBy, options.Value);
            return error ?? Results.Ok(await statistics.GetLatencyAsync(filter!, ct));
        });

        group.MapGet("/endpoints", async (
            Guid organizationId, DateTime? from, DateTime? to, string? routeTemplate,
            string? httpMethod, string? status, string? statusClass, Guid? apiKeyId, Guid? userId,
            int? page, int? pageSize, string? groupBy, IApiStatisticsService statistics,
            IOptions<TelemetryOptions> options, CancellationToken ct) =>
        {
            var (filter, error) = Build(organizationId, from, to, routeTemplate, httpMethod, status,
                statusClass, apiKeyId, userId, page, pageSize, groupBy, options.Value);
            return error ?? Results.Ok(await statistics.GetEndpointsAsync(filter!, ct));
        });

        group.MapGet("/users", async (
            Guid organizationId, DateTime? from, DateTime? to, string? routeTemplate,
            string? httpMethod, string? status, string? statusClass, Guid? apiKeyId, Guid? userId,
            int? page, int? pageSize, string? groupBy, IApiStatisticsService statistics,
            IOptions<TelemetryOptions> options, CancellationToken ct) =>
        {
            var (filter, error) = Build(organizationId, from, to, routeTemplate, httpMethod, status,
                statusClass, apiKeyId, userId, page, pageSize, groupBy, options.Value);
            return error ?? Results.Ok(await statistics.GetUsersAsync(filter!, ct));
        });

        group.MapGet("/quota", async (
            Guid organizationId, IApiStatisticsService statistics, CancellationToken ct) =>
            Results.Ok(await statistics.GetQuotaAsync(organizationId, ct)));

        group.MapGet("/slow-requests", async (
            Guid organizationId, DateTime? from, DateTime? to, string? routeTemplate,
            string? httpMethod, string? status, string? statusClass, Guid? apiKeyId, Guid? userId,
            int? page, int? pageSize, string? groupBy, IApiStatisticsService statistics,
            IOptions<TelemetryOptions> options, CancellationToken ct) =>
        {
            var (filter, error) = Build(organizationId, from, to, routeTemplate, httpMethod, status,
                statusClass, apiKeyId, userId, page, pageSize, groupBy, options.Value);
            return error ?? Results.Ok(await statistics.GetSlowRequestsAsync(filter!, ct));
        });

        group.MapGet("/billable", async (
            Guid organizationId, DateTime? from, DateTime? to, string? routeTemplate,
            string? httpMethod, string? status, string? statusClass, Guid? apiKeyId, Guid? userId,
            int? page, int? pageSize, string? groupBy, IApiStatisticsService statistics,
            IOptions<TelemetryOptions> options, CancellationToken ct) =>
        {
            var (filter, error) = Build(organizationId, from, to, routeTemplate, httpMethod, status,
                statusClass, apiKeyId, userId, page, pageSize, groupBy, options.Value);
            return error ?? Results.Ok(await statistics.GetBillableAsync(filter!, ct));
        });

        endpoints
            .MapGroup("/orgs/{organizationId:guid}/statistics/api-keys")
            .WithTags("API Key Statistics")
            .RequireAuthorization(AuthorizationConfiguration.StatsViewPolicy)
            .MapGet("", async (
                Guid organizationId, DateTime? from, DateTime? to, string? routeTemplate,
                string? httpMethod, string? status, string? statusClass, Guid? apiKeyId, Guid? userId,
                int? page, int? pageSize, string? groupBy, IApiStatisticsService statistics,
                IOptions<TelemetryOptions> options, CancellationToken ct) =>
            {
                var (filter, error) = Build(organizationId, from, to, routeTemplate, httpMethod, status,
                    statusClass, apiKeyId, userId, page, pageSize, groupBy, options.Value);
                return error ?? Results.Ok(await statistics.GetApiKeysAsync(filter!, ct));
            });
    }

    private static void MapAdminEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/admin/statistics/api")
            .WithTags("Admin API Statistics")
            .RequireAuthorization(AuthorizationConfiguration.StatsSystemPolicy);

        group.MapGet("/requests", async (
            DateTime? from, DateTime? to, string? routeTemplate, string? httpMethod, string? status,
            string? statusClass, Guid? apiKeyId, Guid? userId, int? page, int? pageSize,
            string? groupBy, IApiStatisticsService statistics, IOptions<TelemetryOptions> options,
            CancellationToken ct) =>
        {
            var (filter, error) = Build(null, from, to, routeTemplate, httpMethod, status, statusClass,
                apiKeyId, userId, page, pageSize, groupBy, options.Value);
            return error ?? Results.Ok(await statistics.GetRequestsAsync(filter!, ct));
        });

        group.MapGet("/errors", async (
            DateTime? from, DateTime? to, string? routeTemplate, string? httpMethod, string? status,
            string? statusClass, Guid? apiKeyId, Guid? userId, int? page, int? pageSize,
            string? groupBy, IApiStatisticsService statistics, IOptions<TelemetryOptions> options,
            CancellationToken ct) =>
        {
            var (filter, error) = Build(null, from, to, routeTemplate, httpMethod, status, statusClass,
                apiKeyId, userId, page, pageSize, groupBy, options.Value);
            return error ?? Results.Ok(await statistics.GetErrorsAsync(filter!, ct));
        });

        group.MapGet("/latency", async (
            DateTime? from, DateTime? to, string? routeTemplate, string? httpMethod, string? status,
            string? statusClass, Guid? apiKeyId, Guid? userId, int? page, int? pageSize,
            string? groupBy, IApiStatisticsService statistics, IOptions<TelemetryOptions> options,
            CancellationToken ct) =>
        {
            var (filter, error) = Build(null, from, to, routeTemplate, httpMethod, status, statusClass,
                apiKeyId, userId, page, pageSize, groupBy, options.Value);
            return error ?? Results.Ok(await statistics.GetLatencyAsync(filter!, ct));
        });

        group.MapGet("/endpoints", async (
            DateTime? from, DateTime? to, string? routeTemplate, string? httpMethod, string? status,
            string? statusClass, Guid? apiKeyId, Guid? userId, int? page, int? pageSize,
            string? groupBy, IApiStatisticsService statistics, IOptions<TelemetryOptions> options,
            CancellationToken ct) =>
        {
            var (filter, error) = Build(null, from, to, routeTemplate, httpMethod, status, statusClass,
                apiKeyId, userId, page, pageSize, groupBy, options.Value);
            return error ?? Results.Ok(await statistics.GetEndpointsAsync(filter!, ct));
        });

        endpoints
            .MapGroup("/admin/statistics/api-keys")
            .WithTags("Admin API Key Statistics")
            .RequireAuthorization(AuthorizationConfiguration.StatsSystemPolicy)
            .MapGet("", async (
                DateTime? from, DateTime? to, string? routeTemplate, string? httpMethod, string? status,
                string? statusClass, Guid? apiKeyId, Guid? userId, int? page, int? pageSize,
                string? groupBy, IApiStatisticsService statistics, IOptions<TelemetryOptions> options,
                CancellationToken ct) =>
            {
                var (filter, error) = Build(null, from, to, routeTemplate, httpMethod, status, statusClass,
                    apiKeyId, userId, page, pageSize, groupBy, options.Value);
                return error ?? Results.Ok(await statistics.GetApiKeysAsync(filter!, ct));
            });
    }

    private static (ApiStatisticsFilter? Filter, IResult? Error) Build(
        Guid? organizationId, DateTime? from, DateTime? to, string? routeTemplate, string? httpMethod,
        string? status, string? statusClass, Guid? apiKeyId, Guid? userId, int? page, int? pageSize,
        string? groupBy, TelemetryOptions options)
    {
        if (!ApiStatisticsValidation.TryCreateWindow(
                from, to, options.MaxWindowDays, DateTime.UtcNow,
                out var start, out var end, out var windowError))
        {
            return (null, Results.BadRequest(new { message = windowError }));
        }

        if (!ApiStatisticsValidation.TryParseStatusCode(status, out var parsedStatus, out var statusError))
        {
            return (null, Results.BadRequest(new { message = statusError }));
        }

        if (!ApiStatisticsValidation.TryParseStatusClass(statusClass, out var parsedClass, out var classError))
        {
            return (null, Results.BadRequest(new { message = classError }));
        }

        if (!ApiStatisticsValidation.TryParseGroupBy(groupBy, out var parsedGroupBy, out var groupError))
        {
            return (null, Results.BadRequest(new { message = groupError }));
        }

        var (normalizedPage, normalizedPageSize) =
            ApiStatisticsValidation.NormalizePaging(page, pageSize);

        return (new ApiStatisticsFilter(
            organizationId, start, end,
            string.IsNullOrWhiteSpace(routeTemplate) ? null : routeTemplate,
            string.IsNullOrWhiteSpace(httpMethod) ? null : httpMethod,
            string.IsNullOrWhiteSpace(status) ? null : parsedStatus,
            parsedClass,
            apiKeyId, userId,
            normalizedPage, normalizedPageSize, parsedGroupBy), null);
    }
}
