using System.Security.Claims;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Shared.Repositories;
using Aveline.Api.Modules.Statistics.Domain;
using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Services;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Statistics.Endpoints;

/// <summary>
/// The team-only system statistics endpoints (docs/api/README.md §C.8, FR-7.13–FR-7.15).
/// Invalid query values return 400 with <c>{ message }</c>; a metric the host cannot measure
/// is omitted from the payload rather than reported as zero (BR-7.10).
/// </summary>
public static class SystemStatisticsEndpoints
{
    private static readonly string[] AllowedWindowSizes = ["instant", "minute", "hour", "day"];

    public static IEndpointRouteBuilder MapSystemStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/admin/statistics/system")
            .WithTags("System Statistics")
            .RequireAuthorization(AuthorizationConfiguration.StatsSystemPolicy);

        group.MapGet("/overview", async (ISystemStatisticsService statistics, CancellationToken ct) =>
            Results.Ok(await statistics.GetOverviewAsync(ct)));

        group.MapGet("/metrics", async (
            string? metric, DateTime? from, DateTime? to, string? windowSize,
            ISystemStatisticsService statistics, IOptions<TelemetryOptions> options,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(metric))
            {
                return Results.BadRequest(new { message = "The 'metric' query parameter is required." });
            }

            if (!TryParseWindowSize(windowSize, out var parsedWindow, out var windowError))
            {
                return Results.BadRequest(new { message = windowError });
            }

            if (!TryCreateWindow(from, to, options.Value.MaxWindowDays, out var start, out var end, out var error))
            {
                return Results.BadRequest(new { message = error });
            }

            return Results.Ok(await statistics.GetMetricSeriesAsync(metric, start, end, parsedWindow, ct));
        });

        group.MapGet("/queues", async (ISystemStatisticsService statistics, CancellationToken ct) =>
            Results.Ok(await statistics.GetQueuesAsync(ct)));

        group.MapGet("/errors", async (
            DateTime? from, DateTime? to, string? groupBy,
            ISystemStatisticsService statistics, IOptions<TelemetryOptions> options,
            CancellationToken ct) =>
        {
            if (!TryParseWindowSize(groupBy, out var parsedWindow, out var windowError))
            {
                return Results.BadRequest(new { message = windowError });
            }

            if (!TryCreateWindow(from, to, options.Value.MaxWindowDays, out var start, out var end, out var error))
            {
                return Results.BadRequest(new { message = error });
            }

            return Results.Ok(await statistics.GetErrorsAsync(start, end, parsedWindow, ct));
        });

        group.MapGet("/throughput", async (
            DateTime? from, DateTime? to, string? groupBy,
            ISystemStatisticsService statistics, IOptions<TelemetryOptions> options,
            CancellationToken ct) =>
        {
            if (!TryParseWindowSize(groupBy, out var parsedWindow, out var windowError))
            {
                return Results.BadRequest(new { message = windowError });
            }

            if (!TryCreateWindow(from, to, options.Value.MaxWindowDays, out var start, out var end, out var error))
            {
                return Results.BadRequest(new { message = error });
            }

            return Results.Ok(await statistics.GetThroughputAsync(start, end, parsedWindow, ct));
        });

        group.MapGet("/eventbus", async (
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            ISystemStatisticsService statistics,
            CancellationToken ct) =>
            Results.Ok(await statistics.GetEventBusAsync(from, to, ct)));

        group.MapGet("/alerts", async (
            string? status, string? severity, Guid? ruleId, int? page, int? pageSize,
            ISystemStatisticsService statistics, CancellationToken ct) =>
        {
            if (!TryParseStatus(status, out var parsedStatus, out var statusError))
            {
                return Results.BadRequest(new { message = statusError });
            }

            if (!TryParseSeverity(severity, out var parsedSeverity, out var severityError))
            {
                return Results.BadRequest(new { message = severityError });
            }

            var (normalizedPage, normalizedPageSize) = ApiStatisticsValidation.NormalizePaging(page, pageSize);
            return Results.Ok(await statistics.GetAlertsAsync(
                parsedStatus, parsedSeverity, ruleId, normalizedPage, normalizedPageSize, ct));
        });

        group.MapPost("/alerts/{alertId:guid}/acknowledge", async (
            Guid alertId, AcknowledgeAlertRequest? request, ClaimsPrincipal principal,
            IAlertService alerts, IUserRepository users,
            CancellationToken ct) =>
        {
            var clerkId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? principal.FindFirstValue("sub");
            if (string.IsNullOrEmpty(clerkId))
            {
                return Results.Unauthorized();
            }

            var user = await users.GetByClerkIdAsync(clerkId, ct);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var alert = await alerts.AcknowledgeAsync(alertId, user.Id, request?.Note, ct);
            return alert is null ? Results.NotFound() : Results.Ok(alert);
        });

        return endpoints;
    }

    private static bool TryCreateWindow(
        DateTime? from, DateTime? to, int maxWindowDays,
        out DateTime start, out DateTime end, out string? error)
        => ApiStatisticsValidation.TryCreateWindow(from, to, maxWindowDays, DateTime.UtcNow, out start, out end, out error);

    private static bool TryParseWindowSize(string? value, out string windowSize, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            windowSize = "hour";
            error = null;
            return true;
        }

        var normalized = value.ToLowerInvariant();
        if (!AllowedWindowSizes.Contains(normalized))
        {
            windowSize = "hour";
            error = $"Unknown windowSize '{value}'.";
            return false;
        }

        windowSize = normalized;
        error = null;
        return true;
    }

    private static bool TryParseStatus(string? value, out string? status, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            status = null;
            error = null;
            return true;
        }

        if (!Enum.TryParse<AlertStatus>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            status = null;
            error = $"Unknown status '{value}'.";
            return false;
        }

        status = parsed.ToString();
        error = null;
        return true;
    }

    private static bool TryParseSeverity(string? value, out string? severity, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            severity = null;
            error = null;
            return true;
        }

        if (!Enum.TryParse<AlertSeverity>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            severity = null;
            error = $"Unknown severity '{value}'.";
            return false;
        }

        severity = parsed.ToString();
        error = null;
        return true;
    }
}
