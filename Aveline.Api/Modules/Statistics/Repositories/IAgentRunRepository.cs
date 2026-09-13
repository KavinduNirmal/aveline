using Aveline.Api.Modules.Statistics.Models;

namespace Aveline.Api.Modules.Statistics.Repositories;

/// <summary>
/// Filters shared by the paged run list and the unpaged aggregate queries. A non-null
/// <see cref="OrganizationId"/> is always applied: there is no EF global tenant filter,
/// so isolation depends on every call site passing the caller's organisation.
/// </summary>
public sealed record AgentRunFilter(
    Guid? OrganizationId,
    DateTime From,
    DateTime To,
    string? AgentKey = null,
    AgentRunTriggerKind? TriggerKind = null,
    AgentRunStatus? Status = null,
    int Page = 1,
    int PageSize = 50);

/// <summary>Filters for the step-level statistics (S-15, S-17, S-18, S-20).</summary>
public sealed record AgentStepFilter(
    Guid? OrganizationId,
    DateTime From,
    DateTime To,
    string? AgentKey = null);

/// <summary>
/// Data access for <see cref="AgentWorkflowRun"/> and <see cref="AgentStepRun"/>. Every
/// method that accepts an organisation filters on it when it is non-null.
/// </summary>
public interface IAgentRunRepository
{
    /// <summary>Gets one run, scoped to <paramref name="organizationId"/> when supplied.</summary>
    Task<AgentWorkflowRun?> GetRunByIdAsync(Guid? organizationId, Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the run for a <c>(organization, workflowId)</c> pair — the idempotent ingest key
    /// (FR-5.12). Scoped to <paramref name="organizationId"/> when supplied.
    /// </summary>
    Task<AgentWorkflowRun?> GetRunByWorkflowIdAsync(
        Guid? organizationId, string workflowId, CancellationToken cancellationToken);

    /// <summary>Lists runs filtered, ordered newest first, with the total before paging.</summary>
    Task<(IReadOnlyList<AgentWorkflowRun> Items, int Total)> ListRunsAsync(
        AgentRunFilter filter, CancellationToken cancellationToken);

    /// <summary>Returns every matching run (no paging) for in-memory aggregation.</summary>
    Task<IReadOnlyList<AgentWorkflowRun>> QueryRunsAsync(
        AgentRunFilter filter, CancellationToken cancellationToken);

    /// <summary>Returns every matching step (no paging) for in-memory aggregation.</summary>
    Task<IReadOnlyList<AgentStepRun>> QueryStepsAsync(
        AgentStepFilter filter, CancellationToken cancellationToken);

    /// <summary>Steps of one run ordered by <c>StepIndex</c> then <c>AttemptNumber</c> (BR-5.4).</summary>
    Task<IReadOnlyList<AgentStepRun>> ListStepsAsync(Guid workflowRunId, CancellationToken cancellationToken);

    Task AddRunAsync(AgentWorkflowRun run, CancellationToken cancellationToken);

    Task UpdateRunAsync(AgentWorkflowRun run, CancellationToken cancellationToken);

    /// <summary>Removes the run's existing steps and inserts <paramref name="steps"/> in their place.</summary>
    Task ReplaceStepsAsync(
        Guid workflowRunId, IReadOnlyList<AgentStepRun> steps, CancellationToken cancellationToken);

    Task AddStepsAsync(IReadOnlyList<AgentStepRun> steps, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Deletes steps started before <paramref name="cutoff"/> (BR-5.9, 90 days).</summary>
    Task<int> DeleteStepsOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken);

    /// <summary>Deletes runs started before <paramref name="cutoff"/> (BR-5.9, 400 days).</summary>
    Task<int> DeleteRunsOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken);

    /// <summary>Paused runs whose <c>PausedAt</c> is before <paramref name="cutoff"/> and not yet terminal.</summary>
    Task<IReadOnlyList<AgentWorkflowRun>> ListPausedRunsBeforeAsync(
        DateTime cutoff, CancellationToken cancellationToken);

    /// <summary>
    /// Marks every paused run whose <c>PausedAt</c> is before <paramref name="pausedBefore"/> as
    /// <c>TimedOut</c>, setting <c>CompletedAt</c> and <c>DurationMs</c>. Idempotent.
    /// </summary>
    Task<int> MarkTimedOutAsync(
        DateTime pausedBefore, DateTime completedAt, CancellationToken cancellationToken);
}
