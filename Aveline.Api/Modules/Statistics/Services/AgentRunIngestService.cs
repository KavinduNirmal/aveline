using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Statistics.DTOs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// Validates and stores agent run reports (FR-5.1–FR-5.12, BR-5.1–BR-5.9). The run and
/// its steps are the only telemetry persisted; hashes and byte counts stand in for tool
/// arguments and results (FR-5.9).
/// </summary>
public sealed class AgentRunIngestService(
    IAgentRunRepository repository,
    AppDbContext db,
    IConfiguration configuration,
    IEventBus eventBus,
    ILogger<AgentRunIngestService> logger) : IAgentRunIngestService
{
    private const int DefaultMaxStepsPerRun = 200;
    private const int DurationToleranceMs = 5;
    private const int WorkflowIdMaxLength = 128;

    /// <summary>
    /// The registered agent keys (BR-5.5). <c>orchestrator</c> is the top-level graph
    /// pseudo-agent.
    /// </summary>
    private static readonly HashSet<string> RegisteredAgentKeys =
        new(StringComparer.Ordinal)
        {
            "customer_memory", "visual_insight", "commerce", "orchestrator",
        };

    private int MaxStepsPerRun =>
        configuration.GetValue("AgentStats:MaxStepsPerRun", DefaultMaxStepsPerRun);

    public async Task<AgentRunIngestResultDto> ReportRunAsync(
        AgentRunReportRequest request, CancellationToken cancellationToken)
    {
        ValidateWorkflowId(request.WorkflowId);

        var steps = request.Steps ?? [];
        if (steps.Count > MaxStepsPerRun)
        {
            throw new AgentStepCapExceededException(MaxStepsPerRun);
        }

        ValidateSteps(steps);
        ValidateRun(request);

        var organizationId = await ResolveOrganizationAsync(request, cancellationToken);
        var existing = await repository.GetRunByWorkflowIdAsync(
            organizationId, request.WorkflowId, cancellationToken);

        if (existing is not null)
        {
            if (IsTerminal(existing.Status))
            {
                if (!MatchesTerminal(existing, request))
                {
                    throw new AgentRunConflictException(
                        $"Workflow '{request.WorkflowId}' is already terminal ({existing.Status}).");
                }

                return AgentRunIngestResultDto.From(existing, created: false);
            }

            var previousStatus = existing.Status;
            ApplyRun(existing, request, steps, organizationId);
            await repository.ReplaceStepsAsync(
                existing.Id, MapSteps(existing.Id, organizationId, steps), cancellationToken);

            await LinkUsageAsync(existing, request.AiUsageRecordId, cancellationToken);
            await PublishAsync(existing, previousStatus, created: false, cancellationToken);
            return AgentRunIngestResultDto.From(existing, created: false);
        }

        var run = new AgentWorkflowRun { Id = Guid.CreateVersion7(), CreatedAt = DateTime.UtcNow };
        ApplyRun(run, request, steps, organizationId);
        await repository.AddRunAsync(run, cancellationToken);

        if (steps.Count > 0)
        {
            await repository.ReplaceStepsAsync(
                run.Id, MapSteps(run.Id, organizationId, steps), cancellationToken);
        }

        await LinkUsageAsync(run, request.AiUsageRecordId, cancellationToken);
        await PublishAsync(run, previousStatus: null, created: true, cancellationToken);
        return AgentRunIngestResultDto.From(run, created: true);
    }

    public async Task<AgentRunIngestResultDto> AppendStepsAsync(
        string workflowId, AgentStepAppendRequest request, CancellationToken cancellationToken)
    {
        ValidateWorkflowId(workflowId);
        ValidateSteps(request.Steps);

        var run = await repository.GetRunByWorkflowIdAsync(
            request.OrganizationId, workflowId, cancellationToken);

        if (run is null)
        {
            throw new AgentRunNotFoundException($"Workflow '{workflowId}' was not found.");
        }

        if (IsTerminal(run.Status))
        {
            throw new AgentRunConflictException(
                $"Workflow '{workflowId}' is already terminal ({run.Status}).");
        }

        var existing = await repository.ListStepsAsync(run.Id, cancellationToken);
        if (existing.Count + request.Steps.Count > MaxStepsPerRun)
        {
            throw new AgentStepCapExceededException(MaxStepsPerRun);
        }

        var existingKeys = existing
            .Select(step => (step.StepIndex, step.AttemptNumber))
            .ToHashSet();
        if (request.Steps.Any(step => existingKeys.Contains((step.StepIndex, step.AttemptNumber))))
        {
            throw new AgentRunConflictException(
                "A step with the same index and attempt number was already recorded.");
        }

        await repository.AddStepsAsync(
            MapSteps(run.Id, run.OrganizationId, request.Steps), cancellationToken);

        var allSteps = existing.Select(ToPlaceholder).Concat(request.Steps).ToArray();
        run.StepCount = allSteps.Length;
        run.ToolCallCount = allSteps.Count(step => step.StepKind == AgentStepKind.ToolCall);
        run.AgentsInvolved = MergeAgents(run.AgentsInvolved, request.Steps);
        run.UpdatedAt = DateTime.UtcNow;
        await repository.UpdateRunAsync(run, cancellationToken);

        return AgentRunIngestResultDto.From(run, created: false);
    }

    public async Task<AgentRunDetailDto?> GetRunAsync(
        Guid? organizationId, string workflowId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            return null;
        }

        var run = await repository.GetRunByWorkflowIdAsync(
            organizationId, workflowId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var steps = await repository.ListStepsAsync(run.Id, cancellationToken);

        return new AgentRunDetailDto(
            AgentRunSummaryDto.From(run),
            steps.Select(AgentStepDto.From).ToArray(),
            // M-6: derive the flags from the rows actually persisted for this run instead of
            // hard-coding them. A run with no steps, duration or cost stays honestly "false".
            AgentDataQualityDto.Derive([run], steps));
    }

    private static bool IsTerminal(AgentRunStatus status) =>
        status is not (AgentRunStatus.Running or AgentRunStatus.PausedForApproval);

    /// <summary>Terminal runs are immutable; only a byte-identical report is idempotent.</summary>
    private static bool MatchesTerminal(AgentWorkflowRun run, AgentRunReportRequest request) =>
        run.Status == request.Status
        // Compared in UTC, matching what <see cref="ApplyRun"/> persists: an inbound offset-bearing
        // timestamp arrives as Kind=Local, so a raw comparison would misread an identical retry as
        // a conflicting report once the stored value is normalized.
        && run.CompletedAt == ToUtc(request.CompletedAt)
        && string.Equals(run.ErrorCode, request.ErrorCode, StringComparison.Ordinal);

    private static void ValidateWorkflowId(string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            throw new AgentRunValidationException("WorkflowId is required.");
        }

        if (workflowId.Length > WorkflowIdMaxLength)
        {
            throw new AgentRunValidationException(
                $"WorkflowId must be at most {WorkflowIdMaxLength} characters.");
        }
    }

    private static void ValidateRun(AgentRunReportRequest request)
    {
        if (request.StartedAt == default)
        {
            throw new AgentRunValidationException("StartedAt is required.");
        }

        if (request.CompletedAt is { } completed && completed < request.StartedAt)
        {
            throw new AgentRunValidationException("CompletedAt must not precede StartedAt.");
        }

        var nonTerminal = request.Status is AgentRunStatus.Running or AgentRunStatus.PausedForApproval;
        if (nonTerminal && request.CompletedAt is not null)
        {
            throw new AgentRunValidationException(
                "A non-terminal run must not carry CompletedAt.");
        }

        if (request.Status == AgentRunStatus.Succeeded
            && (request.CompletedAt is null || !string.IsNullOrWhiteSpace(request.ErrorCode)))
        {
            throw new AgentRunValidationException(
                "A succeeded run requires CompletedAt and must not carry an ErrorCode.");
        }

        if (!nonTerminal && request.CompletedAt is null)
        {
            throw new AgentRunValidationException(
                $"A terminal run ({request.Status}) requires CompletedAt.");
        }

        if (request.DurationMs is < 0)
        {
            throw new AgentRunValidationException("DurationMs must not be negative.");
        }

        EnsureNonNegative(request.InputTokens, nameof(request.InputTokens));
        EnsureNonNegative(request.OutputTokens, nameof(request.OutputTokens));
        EnsureNonNegative(request.CachedTokens, nameof(request.CachedTokens));
        EnsureNonNegative(request.ToolCallCount, nameof(request.ToolCallCount));
        EnsureNonNegative(request.RetryCount, nameof(request.RetryCount));

        if (request.ActualCostUsd < 0m || request.BlossomUnits < 0m)
        {
            throw new AgentRunValidationException("Costs and Blossom units must not be negative.");
        }
    }

    private static void EnsureNonNegative(int value, string name)
    {
        if (value < 0)
        {
            throw new AgentRunValidationException($"{name} must not be negative.");
        }
    }

    private static void ValidateSteps(IReadOnlyList<AgentStepReportRequest> steps)
    {
        if (steps is null)
        {
            throw new AgentRunValidationException("Steps list must not be null.");
        }

        var keys = new HashSet<(short Index, short Attempt)>();
        foreach (var step in steps)
        {
            if (step.StepIndex < 0)
            {
                throw new AgentRunValidationException("StepIndex must not be negative.");
            }

            if (step.AttemptNumber < 1)
            {
                throw new AgentRunValidationException("AttemptNumber must be at least 1.");
            }

            if (string.IsNullOrWhiteSpace(step.AgentKey))
            {
                throw new AgentRunValidationException("AgentKey is required on every step.");
            }

            if (!RegisteredAgentKeys.Contains(step.AgentKey))
            {
                throw new AgentRunValidationException(
                    $"AgentKey '{step.AgentKey}' is not a registered agent.");
            }

            if (string.IsNullOrWhiteSpace(step.NodeName))
            {
                throw new AgentRunValidationException("NodeName is required on every step.");
            }

            if (step.StartedAt == default)
            {
                throw new AgentRunValidationException("StartedAt is required on every step.");
            }

            if (step.CompletedAt is { } completed && completed < step.StartedAt)
            {
                throw new AgentRunValidationException("A step's CompletedAt must not precede StartedAt.");
            }

            if (!keys.Add((step.StepIndex, step.AttemptNumber)))
            {
                throw new AgentRunValidationException(
                    $"Duplicate step order ({step.StepIndex}, {step.AttemptNumber}).");
            }

            EnsureNonNegative(step.InputTokens, "step.InputTokens");
            EnsureNonNegative(step.OutputTokens, "step.OutputTokens");
            EnsureNonNegative(step.CachedTokens, "step.CachedTokens");

            if (step.ActualCostUsd < 0m)
            {
                throw new AgentRunValidationException("A step's cost must not be negative.");
            }
        }
    }

    private void ApplyRun(
        AgentWorkflowRun run,
        AgentRunReportRequest request,
        IReadOnlyList<AgentStepReportRequest> steps,
        Guid? organizationId)
    {
        var now = DateTime.UtcNow;

        run.OrganizationId = organizationId;
        run.WorkflowId = request.WorkflowId;
        run.ParentWorkflowRunId = request.ParentWorkflowRunId;
        run.RequestId = request.RequestId;
        run.TraceId = request.TraceId;
        run.TriggerKind = request.TriggerKind;
        run.TriggerRef = request.TriggerRef;
        run.ConversationId = request.ConversationId;
        run.CustomerId = request.CustomerId;
        run.InitiatedByUserId = request.InitiatedByUserId;
        run.Status = request.Status;
        // Every timestamp is normalized to UTC: Npgsql rejects non-UTC kinds on
        // `timestamp with time zone`, and an inbound offset-bearing string arrives as Kind=Local.
        run.StartedAt = ToUtc(request.StartedAt);
        run.CompletedAt = ToUtc(request.CompletedAt);
        run.DurationMs = ResolveDuration(request);
        run.PausedAt = request.Status == AgentRunStatus.PausedForApproval
            ? ToUtc(request.PausedAt ?? now)
            : ToUtc(request.PausedAt);
        run.ResumedAt = ToUtc(request.ResumedAt);
        run.ApprovalWaitMs = ResolveApprovalWait(request);
        run.StepCount = steps.Count;
        run.ToolCallCount = request.ToolCallCount > 0
            ? request.ToolCallCount
            : steps.Count(step => step.StepKind == AgentStepKind.ToolCall);
        run.RetryCount = request.RetryCount;
        run.InputTokens = request.InputTokens;
        run.OutputTokens = request.OutputTokens;
        run.CachedTokens = request.CachedTokens;
        run.ActualCostUsd = request.ActualCostUsd;
        run.BlossomUnits = request.BlossomUnits;
        run.PricingRuleId = request.PricingRuleId;
        run.PlanTierAtRun = request.PlanTierAtRun;
        run.IsUnattributed = organizationId is null;
        run.ErrorCode = request.ErrorCode;
        run.AgentsInvolved = MergeAgents(
            (IEnumerable<string>?)request.AgentsInvolved ?? run.AgentsInvolved, steps);
        run.UpdatedAt = now;

        ReconcileStepTokens(request, steps);
    }

    /// <summary>BR-5.2 — recompute DurationMs when it disagrees with the timestamps by &gt; 5 ms.</summary>
    private static int? ResolveDuration(AgentRunReportRequest request)
    {
        if (request.CompletedAt is not { } completed)
        {
            return null;
        }

        var expected = (int)Math.Round(
            (completed - request.StartedAt).TotalMilliseconds, MidpointRounding.AwayFromZero);

        if (request.DurationMs is { } supplied
            && Math.Abs(supplied - expected) <= DurationToleranceMs)
        {
            return supplied;
        }

        return expected;
    }

    private static int? ResolveApprovalWait(AgentRunReportRequest request)
    {
        if (request.PausedAt is { } paused && request.ResumedAt is { } resumed && resumed >= paused)
        {
            return (int)Math.Max(0, (resumed - paused).TotalMilliseconds);
        }

        return request.ApprovalWaitMs;
    }

    /// <summary>
    /// BR-5.6 — the workflow aggregate wins, but a discrepancy against the step sum is
    /// logged so the instrumentation gap is visible.
    /// </summary>
    private void ReconcileStepTokens(
        AgentRunReportRequest request, IReadOnlyList<AgentStepReportRequest> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        var stepInput = steps.Sum(step => step.InputTokens);
        var stepOutput = steps.Sum(step => step.OutputTokens);
        var stepCached = steps.Sum(step => step.CachedTokens);

        if (stepInput != request.InputTokens
            || stepOutput != request.OutputTokens
            || stepCached != request.CachedTokens)
        {
            logger.LogWarning(
                "Agent run {WorkflowId} token aggregate ({Input}/{Output}/{Cached}) differs from its {StepCount} steps ({StepInput}/{StepOutput}/{StepCached}); the aggregate wins (BR-5.6).",
                request.WorkflowId,
                request.InputTokens, request.OutputTokens, request.CachedTokens,
                steps.Count, stepInput, stepOutput, stepCached);
        }
    }

    /// <summary>
    /// Normalizes an inbound timestamp to <see cref="DateTimeKind.Utc"/> before it is written to a
    /// <c>timestamp with time zone</c> column.
    /// </summary>
    /// <remarks>
    /// The agent service serializes timestamps with <c>datetime.now(UTC).isoformat()</c>, i.e.
    /// <c>2026-09-22T20:47:56.123456+00:00</c>. System.Text.Json materializes an offset-bearing
    /// string into <see cref="DateTimeKind.Local"/> (the same instant, a different <c>Kind</c>),
    /// and Npgsql rejects anything that is not UTC for <c>timestamptz</c>:
    /// <c>Cannot write DateTime with Kind=Local to PostgreSQL type 'timestamp with time zone'</c>.
    /// That turned every run report into a 500 and silently discarded all run telemetry.
    ///
    /// <para>
    /// <see cref="DateTimeKind.Local"/> carries a correct instant, so converting it preserves the
    /// value. <see cref="DateTimeKind.Unspecified"/> carries no offset at all and is treated as
    /// UTC, matching the documented contract that the agent service reports UTC.
    /// </para>
    /// </remarks>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>Nullable counterpart of <see cref="ToUtc"/>.</summary>
    private static DateTime? ToUtc(DateTime? value) => value is { } nonNull ? ToUtc(nonNull) : null;

    /// <summary>Steps of a run already persisted, projected for tool-call counting.</summary>
    private static AgentStepReportRequest ToPlaceholder(AgentStepRun step) => new(
        step.StepIndex, step.AgentKey, step.NodeName, step.StepKind, step.ToolName,
        step.Status, step.AttemptNumber, step.StartedAt, step.CompletedAt, step.DurationMs,
        step.Provider, step.Model, step.InputTokens, step.OutputTokens, step.CachedTokens,
        step.ActualCostUsd, step.ArgsHash, step.ResultBytes, step.ErrorCode);

    private static IReadOnlyList<AgentStepRun> MapSteps(
        Guid runId, Guid? organizationId, IReadOnlyList<AgentStepReportRequest> steps) =>
        steps.Select(step => new AgentStepRun
        {
            Id = Guid.CreateVersion7(),
            WorkflowRunId = runId,
            OrganizationId = organizationId,
            StepIndex = step.StepIndex,
            AgentKey = step.AgentKey,
            NodeName = step.NodeName,
            StepKind = step.StepKind,
            ToolName = step.ToolName,
            Status = step.Status,
            AttemptNumber = step.AttemptNumber,
            StartedAt = ToUtc(step.StartedAt),
            CompletedAt = ToUtc(step.CompletedAt),
            DurationMs = step.DurationMs,
            Provider = step.Provider,
            Model = step.Model,
            InputTokens = step.InputTokens,
            OutputTokens = step.OutputTokens,
            CachedTokens = step.CachedTokens,
            ActualCostUsd = step.ActualCostUsd,
            ArgsHash = step.ArgsHash,
            ResultBytes = step.ResultBytes,
            ErrorCode = step.ErrorCode,
            CreatedAt = DateTime.UtcNow,
        }).ToArray();

    private static List<string> MergeAgents(
        IEnumerable<string>? existing, IReadOnlyList<AgentStepReportRequest> steps) =>
        (existing ?? [])
            .Concat(steps.Select(step => step.AgentKey))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

    private async Task<Guid?> ResolveOrganizationAsync(
        AgentRunReportRequest request, CancellationToken cancellationToken)
    {
        if (request.OrganizationId is { } organizationId)
        {
            return organizationId;
        }

        // D-7: fall back to the organisation of an already-recorded usage row for the
        // same workflow; otherwise the run is stored unattributed.
        return await db.AiUsageRecords
            .Where(record => record.WorkflowId == request.WorkflowId)
            .Select(record => (Guid?)record.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>FR-5.5 — link the run to the <see cref="AiUsageRecord"/> it produced.</summary>
    private async Task LinkUsageAsync(
        AgentWorkflowRun run, Guid? usageRecordId, CancellationToken cancellationToken)
    {
        AiUsageRecord? record = null;

        if (usageRecordId is { } id)
        {
            record = await db.AiUsageRecords
                .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        }

        record ??= await db.AiUsageRecords
            .Where(candidate => candidate.WorkflowId == run.WorkflowId)
            .Where(candidate => candidate.OrganizationId == run.OrganizationId)
            .Where(candidate => candidate.AgentWorkflowRunId == null)
            .OrderBy(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (record is null || record.AgentWorkflowRunId == run.Id)
        {
            return;
        }

        record.AgentWorkflowRunId = run.Id;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task PublishAsync(
        AgentWorkflowRun run, AgentRunStatus? previousStatus, bool created, CancellationToken cancellationToken)
    {
        var eventType = run.Status switch
        {
            AgentRunStatus.Running => created || previousStatus != AgentRunStatus.PausedForApproval
                ? "agent.run.started"
                : "agent.run.resumed",
            AgentRunStatus.Succeeded => "agent.run.completed",
            AgentRunStatus.PausedForApproval => "agent.run.paused",
            _ => "agent.run.failed",
        };

        try
        {
            await eventBus.PublishAsync(
                eventType,
                run.OrganizationId,
                new
                {
                    runId = run.Id,
                    workflowId = run.WorkflowId,
                    status = run.Status.ToString(),
                },
                run.TraceId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            // Telemetry must never fail ingestion; the run row is the source of truth.
            logger.LogWarning(exception,
                "Failed to publish {EventType} for agent run {WorkflowId}.",
                eventType, run.WorkflowId);
        }
    }
}
