using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;

namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// The window and dimensions shared by every agentic statistic (S-13…S-23). A non-null
/// <see cref="OrganizationId"/> scopes the query to that organisation; <c>null</c> is the
/// system-wide view used by the admin route.
/// </summary>
public sealed record AgentStatisticsQuery(
    Guid? OrganizationId,
    DateTime From,
    DateTime To,
    string? AgentKey = null,
    AgentRunTriggerKind? TriggerKind = null,
    AgentRunStatus? Status = null,
    int Page = 1,
    int PageSize = 50);

/// <summary>
/// Read model for the agentic statistics catalog (statistics-catalog.md §5). Every response
/// carries <see cref="AgentDataQualityDto"/> so callers can tell a real measurement from an
/// un-instrumented zero.
/// </summary>
public interface IAgentStatisticsService
{
    /// <summary>S-13 — the paged run list.</summary>
    Task<AgentRunPageDto> GetRunsAsync(AgentStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>S-22 — one run with its ordered step trace, or <c>null</c> when out of scope.</summary>
    Task<AgentRunDetailDto?> GetRunDetailAsync(
        Guid? organizationId, Guid runId, CancellationToken cancellationToken);

    /// <summary>S-14 — success rate over terminal runs; running and paused are excluded.</summary>
    Task<AgentReliabilityDto> GetReliabilityAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>S-16 — end-to-end latency; null series until duration is instrumented.</summary>
    Task<AgentLatencyDto> GetLatencyAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>S-17 — per agent/node step latency.</summary>
    Task<AgentStepLatencyDto> GetStepsAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>S-18 — token usage by agent, provider and model.</summary>
    Task<AgentTokensDto> GetTokensAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>S-19 — total and average actual cost.</summary>
    Task<AgentCostDto> GetCostAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>S-20 — tool usage, success rate and latency.</summary>
    Task<AgentToolsDto> GetToolsAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>S-15 — failures grouped by cause.</summary>
    Task<AgentFailuresDto> GetFailuresAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>S-21 — approval wait time and pending count.</summary>
    Task<AgentApprovalsDto> GetApprovalsAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>Compact system-wide overview for the admin route.</summary>
    Task<AgentOverviewDto> GetOverviewAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken);
}
