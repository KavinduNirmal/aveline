using Aveline.Api.Modules.Statistics.Domain;
using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Repositories;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// Implements the agentic statistics catalog (S-13…S-23) over a caller-supplied window.
/// </summary>
/// <remarks>
/// All aggregation here is on the fly. A <c>DailyAgentMetrics</c> rollup **does** exist
/// (<c>AgentStatsRollupJob</c> writes it daily; statistics-catalog.md §9); this service predates it
/// and keeps the on-the-fly path, which is sufficient at current volumes. The business-KPI usage
/// read (S-48) is the rollup's reader.
/// </remarks>
public sealed class AgentStatisticsService(
    IAgentRunRepository repository,
    IConfiguration configuration) : IAgentStatisticsService
{
    private const int DefaultMinSampleForPercentile = 20;

    private int MinSampleForPercentile =>
        configuration.GetValue("AgentStats:MinSampleForPercentile", DefaultMinSampleForPercentile);

    public async Task<AgentRunPageDto> GetRunsAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken)
    {
        var filter = RunFilter(query);
        var (items, total) = await repository.ListRunsAsync(filter, cancellationToken);

        return new AgentRunPageDto(
            items.Select(AgentRunSummaryDto.From).ToArray(),
            Math.Max(1, query.Page),
            Math.Clamp(query.PageSize, 1, 200),
            total,
            AgentDataQualityDto.Uninstrumented);
    }

    public async Task<AgentRunDetailDto?> GetRunDetailAsync(
        Guid? organizationId, Guid runId, CancellationToken cancellationToken)
    {
        var run = await repository.GetRunByIdAsync(organizationId, runId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var steps = await repository.ListStepsAsync(run.Id, cancellationToken);

        return new AgentRunDetailDto(
            AgentRunSummaryDto.From(run),
            steps.Select(AgentStepDto.From).ToArray(),
            AgentDataQualityDto.Uninstrumented);
    }

    public async Task<AgentReliabilityDto> GetReliabilityAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken)
    {
        var runs = await repository.QueryRunsAsync(RunFilter(query), cancellationToken);

        var succeeded = runs.Count(run => run.Status == AgentRunStatus.Succeeded);
        var failed = runs.Count(run => run.Status == AgentRunStatus.Failed);
        var timedOut = runs.Count(run => run.Status == AgentRunStatus.TimedOut);
        var cancelled = runs.Count(run => run.Status == AgentRunStatus.Cancelled);
        var running = runs.Count(run => run.Status == AgentRunStatus.Running);
        var paused = runs.Count(run => run.Status == AgentRunStatus.PausedForApproval);

        // S-14: running and paused runs are excluded from the denominator.
        var denominator = succeeded + failed + timedOut + cancelled;
        var successRate = denominator == 0 ? (double?)null : (double)succeeded / denominator;

        return new AgentReliabilityDto(
            runs.Count, succeeded, failed, timedOut, cancelled, running, paused,
            denominator, successRate, AgentDataQualityDto.Uninstrumented);
    }

    public async Task<AgentLatencyDto> GetLatencyAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken)
    {
        var runs = await repository.QueryRunsAsync(RunFilter(query), cancellationToken);

        var samples = runs
            .Where(run => run.DurationMs.HasValue)
            .Select(run => (run.StartedAt, Duration: (double)run.DurationMs!.Value))
            .ToArray();

        // No measurement is not the same as a zero measurement (gap G-3): keep everything null.
        if (samples.Length == 0)
        {
            return new AgentLatencyDto(
                0, null, null, null, null, [], AgentDataQualityDto.Uninstrumented);
        }

        var series = samples
            .GroupBy(sample => sample.StartedAt.Date)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var values = group.Select(sample => sample.Duration).ToArray();
                var enough = values.Length >= MinSampleForPercentile;
                return new AgentLatencyBucketDto(
                    group.Key,
                    values.Length,
                    enough ? values.Average() : null,
                    enough ? PercentileCalculator.Percentile(values, 0.50) : null,
                    enough ? PercentileCalculator.Percentile(values, 0.95) : null,
                    enough ? PercentileCalculator.Percentile(values, 0.99) : null);
            })
            .ToArray();

        var all = samples.Select(sample => sample.Duration).ToArray();
        var meetsFloor = all.Length >= MinSampleForPercentile;

        return new AgentLatencyDto(
            all.Length,
            meetsFloor ? all.Average() : null,
            meetsFloor ? PercentileCalculator.Percentile(all, 0.50) : null,
            meetsFloor ? PercentileCalculator.Percentile(all, 0.95) : null,
            meetsFloor ? PercentileCalculator.Percentile(all, 0.99) : null,
            series,
            AgentDataQualityDto.Uninstrumented);
    }

    public async Task<AgentStepLatencyDto> GetStepsAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken)
    {
        var steps = await repository.QueryStepsAsync(StepFilter(query), cancellationToken);

        var items = steps
            .GroupBy(step => new { step.AgentKey, step.NodeName })
            .Select(group =>
            {
                var durations = group
                    .Where(step => step.DurationMs.HasValue)
                    .Select(step => (double)step.DurationMs!.Value)
                    .ToArray();

                var enough = durations.Length >= MinSampleForPercentile;

                return new AgentStepLatencyItemDto(
                    group.Key.AgentKey,
                    group.Key.NodeName,
                    durations.Length,
                    durations.Length > 0 ? durations.Average() : null,
                    enough ? PercentileCalculator.Percentile(durations, 0.95) : null);
            })
            .OrderByDescending(item => item.SampleSize)
            .ThenBy(item => item.AgentKey, StringComparer.Ordinal)
            .ThenBy(item => item.NodeName, StringComparer.Ordinal)
            .ToArray();

        return new AgentStepLatencyDto(items, AgentDataQualityDto.Uninstrumented);
    }

    public async Task<AgentTokensDto> GetTokensAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken)
    {
        var steps = await repository.QueryStepsAsync(StepFilter(query), cancellationToken);

        var items = steps
            .GroupBy(step => new { step.AgentKey, step.Provider, step.Model })
            .Select(group => new AgentTokenUsageItemDto(
                group.Key.AgentKey,
                group.Key.Provider,
                group.Key.Model,
                group.Sum(step => step.InputTokens),
                group.Sum(step => step.OutputTokens),
                group.Sum(step => step.CachedTokens),
                group.Sum(step => step.InputTokens + step.OutputTokens + step.CachedTokens)))
            .OrderByDescending(item => item.TotalTokens)
            .ThenBy(item => item.AgentKey, StringComparer.Ordinal)
            .ToArray();

        return new AgentTokensDto(
            items,
            items.Sum(item => item.InputTokens),
            items.Sum(item => item.OutputTokens),
            items.Sum(item => item.CachedTokens),
            items.Sum(item => item.TotalTokens),
            AgentDataQualityDto.Uninstrumented);
    }

    public async Task<AgentCostDto> GetCostAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken)
    {
        var runs = await repository.QueryRunsAsync(RunFilter(query), cancellationToken);

        var total = runs.Sum(run => run.ActualCostUsd);
        var avg = runs.Count == 0 ? (double?)null : (double)(total / runs.Count);

        return new AgentCostDto(total, avg, runs.Count, AgentDataQualityDto.Uninstrumented);
    }

    public async Task<AgentToolsDto> GetToolsAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken)
    {
        var steps = await repository.QueryStepsAsync(StepFilter(query), cancellationToken);

        var calls = steps
            .Where(step => step.StepKind == AgentStepKind.ToolCall
                           && !string.IsNullOrWhiteSpace(step.ToolName))
            .ToArray();

        var items = calls
            .GroupBy(step => step.ToolName!)
            .Select(group =>
            {
                var durations = group
                    .Where(step => step.DurationMs.HasValue)
                    .Select(step => (double)step.DurationMs!.Value)
                    .ToArray();

                var succeeded = group.Count(step => step.Status == AgentStepStatus.Succeeded);

                return new AgentToolUsageItemDto(
                    group.Key,
                    group.Count(),
                    (double)succeeded / group.Count(),
                    durations.Length > 0 ? durations.Average() : null);
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.ToolName, StringComparer.Ordinal)
            .ToArray();

        return new AgentToolsDto(items, AgentDataQualityDto.Uninstrumented);
    }

    public async Task<AgentFailuresDto> GetFailuresAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken)
    {
        var runs = await repository.QueryRunsAsync(RunFilter(query), cancellationToken);
        var steps = await repository.QueryStepsAsync(StepFilter(query), cancellationToken);

        var items = new List<AgentFailureItemDto>();

        // Run-level failures have no node or agent attribution.
        items.AddRange(runs
            .Where(run => run.Status == AgentRunStatus.Failed)
            .GroupBy(run => run.ErrorCode)
            .Select(group => new AgentFailureItemDto(group.Key, null, null, null, group.Count())));

        // Step-level failures carry the node attribution that G-2 will make reliable.
        items.AddRange(steps
            .Where(step => step.Status == AgentStepStatus.Failed)
            .GroupBy(step => new { step.ErrorCode, step.AgentKey, step.NodeName, step.StepKind })
            .Select(group => new AgentFailureItemDto(
                group.Key.ErrorCode,
                group.Key.AgentKey,
                group.Key.NodeName,
                group.Key.StepKind.ToString(),
                group.Count())));

        return new AgentFailuresDto(
            items
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.ErrorCode, StringComparer.Ordinal)
                .ToArray(),
            AgentDataQualityDto.Uninstrumented);
    }

    public async Task<AgentApprovalsDto> GetApprovalsAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken)
    {
        var runs = await repository.QueryRunsAsync(RunFilter(query), cancellationToken);

        var waits = runs
            .Where(run => run.ApprovalWaitMs.HasValue)
            .Select(run => (double)run.ApprovalWaitMs!.Value)
            .ToArray();

        var pending = runs.Count(run => run.Status == AgentRunStatus.PausedForApproval);
        var enough = waits.Length >= MinSampleForPercentile;

        return new AgentApprovalsDto(
            waits.Length,
            waits.Length > 0 ? waits.Average() : null,
            enough ? PercentileCalculator.Percentile(waits, 0.95) : null,
            pending,
            AgentDataQualityDto.Uninstrumented);
    }

    public async Task<AgentOverviewDto> GetOverviewAsync(
        AgentStatisticsQuery query, CancellationToken cancellationToken)
    {
        var runs = await repository.QueryRunsAsync(RunFilter(query), cancellationToken);

        var succeeded = runs.Count(run => run.Status == AgentRunStatus.Succeeded);
        var failed = runs.Count(run => run.Status == AgentRunStatus.Failed);
        var timedOut = runs.Count(run => run.Status == AgentRunStatus.TimedOut);
        var cancelled = runs.Count(run => run.Status == AgentRunStatus.Cancelled);
        var denominator = succeeded + failed + timedOut + cancelled;

        return new AgentOverviewDto(
            runs.Count,
            runs.Count(run => run.Status == AgentRunStatus.Running),
            runs.Count(run => run.Status == AgentRunStatus.PausedForApproval),
            succeeded,
            failed,
            denominator == 0 ? null : (double)succeeded / denominator,
            AgentDataQualityDto.Uninstrumented);
    }

    private static AgentRunFilter RunFilter(AgentStatisticsQuery query) => new(
        query.OrganizationId,
        query.From,
        query.To,
        query.AgentKey,
        query.TriggerKind,
        query.Status,
        query.Page,
        query.PageSize);

    private static AgentStepFilter StepFilter(AgentStatisticsQuery query) => new(
        query.OrganizationId,
        query.From,
        query.To,
        query.AgentKey);
}
