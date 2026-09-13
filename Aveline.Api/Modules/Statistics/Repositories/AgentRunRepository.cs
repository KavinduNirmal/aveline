using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Statistics.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAgentRunRepository"/>. Queries are translated to
/// SQL where possible; the statistics service aggregates percentiles in memory over the
/// bounded window because there is no rollup table yet (statistics-catalog.md §9).
/// </summary>
public sealed class AgentRunRepository(AppDbContext db) : IAgentRunRepository
{
    private const int MaxPageSize = 200;

    public async Task<AgentWorkflowRun?> GetRunByIdAsync(
        Guid? organizationId, Guid runId, CancellationToken cancellationToken)
    {
        var query = Scoped(db.AgentWorkflowRuns, organizationId);
        return await query.FirstOrDefaultAsync(run => run.Id == runId, cancellationToken);
    }

    public async Task<AgentWorkflowRun?> GetRunByWorkflowIdAsync(
        Guid? organizationId, string workflowId, CancellationToken cancellationToken)
    {
        var query = Scoped(db.AgentWorkflowRuns, organizationId);
        return await query.FirstOrDefaultAsync(run => run.WorkflowId == workflowId, cancellationToken);
    }

    public async Task<(IReadOnlyList<AgentWorkflowRun> Items, int Total)> ListRunsAsync(
        AgentRunFilter filter, CancellationToken cancellationToken)
    {
        var query = Filter(db.AgentWorkflowRuns.AsNoTracking(), filter);

        var total = await query.CountAsync(cancellationToken);

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var items = await query
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<AgentWorkflowRun>> QueryRunsAsync(
        AgentRunFilter filter, CancellationToken cancellationToken)
    {
        var query = Filter(db.AgentWorkflowRuns.AsNoTracking(), filter);
        return await query
            .OrderByDescending(run => run.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentStepRun>> QueryStepsAsync(
        AgentStepFilter filter, CancellationToken cancellationToken)
    {
        var query = db.AgentStepRuns.AsNoTracking()
            .Where(step => step.StartedAt >= filter.From && step.StartedAt <= filter.To);

        if (filter.OrganizationId is { } organizationId)
        {
            query = query.Where(step => step.OrganizationId == organizationId);
        }

        if (!string.IsNullOrWhiteSpace(filter.AgentKey))
        {
            query = query.Where(step => step.AgentKey == filter.AgentKey);
        }

        return await query
            .OrderBy(step => step.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentStepRun>> ListStepsAsync(
        Guid workflowRunId, CancellationToken cancellationToken) =>
        await db.AgentStepRuns.AsNoTracking()
            .Where(step => step.WorkflowRunId == workflowRunId)
            .OrderBy(step => step.StepIndex)
            .ThenBy(step => step.AttemptNumber)
            .ToListAsync(cancellationToken);

    public async Task AddRunAsync(AgentWorkflowRun run, CancellationToken cancellationToken)
    {
        db.AgentWorkflowRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateRunAsync(AgentWorkflowRun run, CancellationToken cancellationToken)
    {
        db.AgentWorkflowRuns.Update(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReplaceStepsAsync(
        Guid workflowRunId, IReadOnlyList<AgentStepRun> steps, CancellationToken cancellationToken)
    {
        var existing = await db.AgentStepRuns
            .Where(step => step.WorkflowRunId == workflowRunId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            db.AgentStepRuns.RemoveRange(existing);
        }

        if (steps.Count > 0)
        {
            db.AgentStepRuns.AddRange(steps);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddStepsAsync(IReadOnlyList<AgentStepRun> steps, CancellationToken cancellationToken)
    {
        if (steps.Count == 0)
        {
            return;
        }

        db.AgentStepRuns.AddRange(steps);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task<int> DeleteStepsOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken)
    {
        var steps = await db.AgentStepRuns
            .Where(step => step.StartedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (steps.Count == 0)
        {
            return 0;
        }

        db.AgentStepRuns.RemoveRange(steps);
        await db.SaveChangesAsync(cancellationToken);
        return steps.Count;
    }

    public async Task<int> DeleteRunsOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken)
    {
        // Steps are included so the cascade is exercised for providers that do not
        // delete untracked dependents.
        var runs = await db.AgentWorkflowRuns
            .Include(run => run.Steps)
            .Where(run => run.StartedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (runs.Count == 0)
        {
            return 0;
        }

        db.AgentWorkflowRuns.RemoveRange(runs);
        await db.SaveChangesAsync(cancellationToken);
        return runs.Count;
    }

    public async Task<IReadOnlyList<AgentWorkflowRun>> ListPausedRunsBeforeAsync(
        DateTime cutoff, CancellationToken cancellationToken) =>
        await db.AgentWorkflowRuns
            .Where(run => run.Status == AgentRunStatus.PausedForApproval)
            .Where(run => run.PausedAt != null && run.PausedAt < cutoff)
            .OrderBy(run => run.PausedAt)
            .ToListAsync(cancellationToken);

    public async Task<int> MarkTimedOutAsync(
        DateTime pausedBefore, DateTime completedAt, CancellationToken cancellationToken)
    {
        var stale = await db.AgentWorkflowRuns
            .Where(run => run.Status == AgentRunStatus.PausedForApproval)
            .Where(run => run.PausedAt != null && run.PausedAt < pausedBefore)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        foreach (var run in stale)
        {
            run.Status = AgentRunStatus.TimedOut;
            run.CompletedAt = completedAt;
            run.DurationMs = (int)Math.Max(0, (completedAt - run.StartedAt).TotalMilliseconds);
            if (run.PausedAt is { } pausedAt)
            {
                run.ApprovalWaitMs = (int)Math.Max(0, (completedAt - pausedAt).TotalMilliseconds);
            }

            run.ErrorCode ??= "approval_timeout";
            run.UpdatedAt = completedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }

    private static IQueryable<AgentWorkflowRun> Scoped(
        IQueryable<AgentWorkflowRun> query, Guid? organizationId) =>
        organizationId is { } org ? query.Where(run => run.OrganizationId == org) : query;

    private static IQueryable<AgentWorkflowRun> Filter(
        IQueryable<AgentWorkflowRun> query, AgentRunFilter filter)
    {
        query = query.Where(run => run.StartedAt >= filter.From && run.StartedAt <= filter.To);

        if (filter.OrganizationId is { } organizationId)
        {
            query = query.Where(run => run.OrganizationId == organizationId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(run => run.Status == status);
        }

        if (filter.TriggerKind is { } triggerKind)
        {
            query = query.Where(run => run.TriggerKind == triggerKind);
        }

        if (!string.IsNullOrWhiteSpace(filter.AgentKey))
        {
            var agentKey = filter.AgentKey;
            query = query.Where(run => run.AgentsInvolved.Contains(agentKey));
        }

        return query;
    }
}
