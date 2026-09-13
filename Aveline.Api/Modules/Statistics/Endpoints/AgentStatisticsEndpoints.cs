using Aveline.Api.Configurations;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Services;

namespace Aveline.Api.Modules.Statistics.Endpoints;

/// <summary>
/// Organisation-facing agentic statistics (docs/api/README.md §C.6) and the team-only
/// admin subset. Every response carries a <c>dataQuality</c> envelope so an
/// un-instrumented zero is never mistaken for a measurement.
/// </summary>
public static class AgentStatisticsEndpoints
{
    private const int MaxWindowDays = 400;

    public static IEndpointRouteBuilder MapAgentStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapOrgEndpoints(endpoints);
        MapAdminEndpoints(endpoints);
        return endpoints;
    }

    private static void MapOrgEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/orgs/{organizationId:guid}/statistics/agents")
            .WithTags("Agent Statistics")
            .RequireAuthorization(AuthorizationConfiguration.StatsAgentPolicy);

        group.MapGet("/runs", async (
            Guid organizationId, DateTime? from, DateTime? to, string? status, string? triggerKind,
            string? agentKey, int? page, int? pageSize, IAgentStatisticsService statistics,
            CancellationToken ct) =>
        {
            var (query, error) = Build(organizationId, from, to, status, triggerKind, agentKey, page, pageSize);
            return error ?? Results.Ok(await statistics.GetRunsAsync(query!, ct));
        });

        group.MapGet("/runs/{runId:guid}", async (
            Guid organizationId, Guid runId, IAgentStatisticsService statistics, CancellationToken ct) =>
        {
            var detail = await statistics.GetRunDetailAsync(organizationId, runId, ct);
            return detail is null
                ? Results.NotFound(new { message = "Agent run not found." })
                : Results.Ok(detail);
        });

        group.MapGet("/reliability", async (
            Guid organizationId, DateTime? from, DateTime? to, string? status, string? triggerKind,
            string? agentKey, IAgentStatisticsService statistics, CancellationToken ct) =>
        {
            var (query, error) = Build(organizationId, from, to, status, triggerKind, agentKey, null, null);
            return error ?? Results.Ok(await statistics.GetReliabilityAsync(query!, ct));
        });

        group.MapGet("/latency", async (
            Guid organizationId, DateTime? from, DateTime? to, string? status, string? triggerKind,
            string? agentKey, IAgentStatisticsService statistics, CancellationToken ct) =>
        {
            var (query, error) = Build(organizationId, from, to, status, triggerKind, agentKey, null, null);
            return error ?? Results.Ok(await statistics.GetLatencyAsync(query!, ct));
        });

        group.MapGet("/steps", async (
            Guid organizationId, DateTime? from, DateTime? to, string? agentKey,
            IAgentStatisticsService statistics, CancellationToken ct) =>
        {
            var (query, error) = Build(organizationId, from, to, null, null, agentKey, null, null);
            return error ?? Results.Ok(await statistics.GetStepsAsync(query!, ct));
        });

        group.MapGet("/tokens", async (
            Guid organizationId, DateTime? from, DateTime? to, string? agentKey,
            IAgentStatisticsService statistics, CancellationToken ct) =>
        {
            var (query, error) = Build(organizationId, from, to, null, null, agentKey, null, null);
            return error ?? Results.Ok(await statistics.GetTokensAsync(query!, ct));
        });

        group.MapGet("/cost", async (
            Guid organizationId, DateTime? from, DateTime? to, string? agentKey, string? status,
            string? triggerKind, IAgentStatisticsService statistics, CancellationToken ct) =>
        {
            var (query, error) = Build(organizationId, from, to, status, triggerKind, agentKey, null, null);
            return error ?? Results.Ok(await statistics.GetCostAsync(query!, ct));
        });

        group.MapGet("/tools", async (
            Guid organizationId, DateTime? from, DateTime? to, string? agentKey,
            IAgentStatisticsService statistics, CancellationToken ct) =>
        {
            var (query, error) = Build(organizationId, from, to, null, null, agentKey, null, null);
            return error ?? Results.Ok(await statistics.GetToolsAsync(query!, ct));
        });

        group.MapGet("/failures", async (
            Guid organizationId, DateTime? from, DateTime? to, string? agentKey, string? status,
            string? triggerKind, IAgentStatisticsService statistics, CancellationToken ct) =>
        {
            var (query, error) = Build(organizationId, from, to, status, triggerKind, agentKey, null, null);
            return error ?? Results.Ok(await statistics.GetFailuresAsync(query!, ct));
        });

        group.MapGet("/approvals", async (
            Guid organizationId, DateTime? from, DateTime? to, string? agentKey,
            IAgentStatisticsService statistics, CancellationToken ct) =>
        {
            var (query, error) = Build(organizationId, from, to, null, null, agentKey, null, null);
            return error ?? Results.Ok(await statistics.GetApprovalsAsync(query!, ct));
        });
    }

    private static void MapAdminEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/admin/statistics/agents")
            .WithTags("Admin Agent Statistics")
            .RequireAuthorization(AuthorizationConfiguration.StatsSystemPolicy);

        group.MapGet("/overview", async (
            DateTime? from, DateTime? to, string? agentKey, string? status, string? triggerKind,
            IAgentStatisticsService statistics, CancellationToken ct) =>
        {
            var (query, error) = Build(null, from, to, status, triggerKind, agentKey, null, null);
            return error ?? Results.Ok(await statistics.GetOverviewAsync(query!, ct));
        });

        group.MapGet("/runs", async (
            DateTime? from, DateTime? to, string? agentKey, string? status, string? triggerKind,
            int? page, int? pageSize, IAgentStatisticsService statistics, CancellationToken ct) =>
        {
            var (query, error) = Build(null, from, to, status, triggerKind, agentKey, page, pageSize);
            return error ?? Results.Ok(await statistics.GetRunsAsync(query!, ct));
        });

        group.MapGet("/reliability", async (
            DateTime? from, DateTime? to, string? agentKey, string? status, string? triggerKind,
            IAgentStatisticsService statistics, CancellationToken ct) =>
        {
            var (query, error) = Build(null, from, to, status, triggerKind, agentKey, null, null);
            return error ?? Results.Ok(await statistics.GetReliabilityAsync(query!, ct));
        });
    }

    private static (AgentStatisticsQuery? Query, IResult? Error) Build(
        Guid? organizationId, DateTime? from, DateTime? to, string? status, string? triggerKind,
        string? agentKey, int? page, int? pageSize)
    {
        var end = to ?? DateTime.UtcNow;
        var start = from ?? end.AddDays(-30);

        if (end <= start)
        {
            return (null, Results.BadRequest(new { message = "The 'to' value must be after 'from'." }));
        }

        if ((end - start).TotalDays > MaxWindowDays)
        {
            return (null, Results.BadRequest(new
            {
                message = $"The statistics window must be at most {MaxWindowDays} days.",
            }));
        }

        AgentRunStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AgentRunStatus>(status, ignoreCase: true, out var value)
                || !Enum.IsDefined(value))
            {
                return (null, Results.BadRequest(new { message = $"Unknown status '{status}'." }));
            }

            parsedStatus = value;
        }

        AgentRunTriggerKind? parsedTrigger = null;
        if (!string.IsNullOrWhiteSpace(triggerKind))
        {
            if (!Enum.TryParse<AgentRunTriggerKind>(triggerKind, ignoreCase: true, out var value)
                || !Enum.IsDefined(value))
            {
                return (null, Results.BadRequest(new { message = $"Unknown triggerKind '{triggerKind}'." }));
            }

            parsedTrigger = value;
        }

        return (new AgentStatisticsQuery(
            organizationId,
            start,
            end,
            string.IsNullOrWhiteSpace(agentKey) ? null : agentKey,
            parsedTrigger,
            parsedStatus,
            page ?? 1,
            pageSize ?? 50), null);
    }
}
