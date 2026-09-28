using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Jobs;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #213 — the agent statistics retention and stale-run jobs. Both are idempotent and
/// respect their configured cutoffs (BR-5.9, FR-5.10, edge case "paused run never resumed").
/// </summary>
public class AgentStatsJobsTests
{
    private static (ServiceProvider Provider, AppDbContext Context) BuildProvider()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"AgentStatsJobs_{Guid.NewGuid()}")
            .Options);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentStats:StepRetentionDays"] = "90",
                ["AgentStats:RunRetentionDays"] = "400",
                ["AgentStats:PausedRunTimeoutHours"] = "72",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<IAgentRunRepository, AgentRunRepository>();
        services.AddSingleton<IDistributedJobLock>(new InMemoryDistributedJobLock());

        return (services.BuildServiceProvider(), context);
    }

    private static AgentWorkflowRun Run(
        DateTime startedAt,
        AgentRunStatus status = AgentRunStatus.Succeeded,
        DateTime? pausedAt = null) => new()
    {
        OrganizationId = Guid.CreateVersion7(),
        WorkflowId = $"wf-{Guid.NewGuid():N}",
        Status = status,
        StartedAt = startedAt,
        CompletedAt = status is AgentRunStatus.Running or AgentRunStatus.PausedForApproval
            ? null
            : startedAt.AddSeconds(1),
        PausedAt = pausedAt,
        IsUnattributed = false,
    };

    private static AgentStepRun Step(Guid runId, DateTime startedAt) => new()
    {
        WorkflowRunId = runId,
        OrganizationId = Guid.CreateVersion7(),
        StepIndex = 0,
        AgentKey = "orchestrator",
        NodeName = "plan",
        StartedAt = startedAt,
    };

    private static AgentStatsRetentionJob RetentionJob(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<IDistributedJobLock>(),
        NullLogger<AgentStatsRetentionJob>.Instance);

    private static StaleAgentRunJob StaleJob(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<IDistributedJobLock>(),
        NullLogger<StaleAgentRunJob>.Instance);

    [Fact]
    public async Task RetentionJob_DeletesOnlyWhatIsPastItsCutoff()
    {
        var (provider, context) = BuildProvider();
        var now = DateTime.UtcNow;

        var recent = Run(now.AddDays(-1));
        var oldRun = Run(now.AddDays(-100));
        var ancient = Run(now.AddDays(-401));
        context.AgentWorkflowRuns.AddRange(recent, oldRun, ancient);

        context.AgentStepRuns.AddRange(
            Step(recent.Id, now.AddDays(-1)),
            Step(oldRun.Id, now.AddDays(-100)),
            Step(ancient.Id, now.AddDays(-401)));
        await context.SaveChangesAsync();

        var processed = await RetentionJob(provider).RunAsync(CancellationToken.None);

        Assert.Equal(3, processed); // two old steps plus one ancient run

        context.ChangeTracker.Clear();
        Assert.Equal(0, await context.AgentStepRuns.CountAsync(step => step.WorkflowRunId == oldRun.Id));
        Assert.Equal(1, await context.AgentStepRuns.CountAsync(step => step.WorkflowRunId == recent.Id));
        Assert.False(await context.AgentWorkflowRuns.AnyAsync(run => run.Id == ancient.Id));
        Assert.True(await context.AgentWorkflowRuns.AnyAsync(run => run.Id == oldRun.Id));
        Assert.True(await context.AgentWorkflowRuns.AnyAsync(run => run.Id == recent.Id));
    }

    [Fact]
    public async Task RetentionJob_IsIdempotent()
    {
        var (provider, context) = BuildProvider();
        var now = DateTime.UtcNow;

        var ancient = Run(now.AddDays(-401));
        context.AgentWorkflowRuns.Add(ancient);
        context.AgentStepRuns.Add(Step(ancient.Id, now.AddDays(-401)));
        await context.SaveChangesAsync();

        var job = RetentionJob(provider);
        await job.RunAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var secondRun = await job.RunAsync(CancellationToken.None);

        Assert.Equal(0, secondRun);
    }

    [Fact]
    public async Task StaleJob_MarksAbandonedRunsAsTimedOut_AndLeavesLiveOnesAlone()
    {
        // Two kinds of abandoned run, and they are swept for different reasons:
        //   * a pause nobody answered (PausedRunTimeoutHours, default 72), and
        //   * a run whose process died before it reported (RunningRunTimeoutHours, default 1).
        //
        // The second only became reachable when the agent started opening a `Running` row at the
        // start of a run (ADR-027). Without it that row would hold `agent.runs_running` above zero
        // forever - a permanent false alarm on the panel that exists to say whether work is in
        // flight.
        var (provider, context) = BuildProvider();
        var now = DateTime.UtcNow;

        var stalePause = Run(now.AddHours(-100), AgentRunStatus.PausedForApproval, pausedAt: now.AddHours(-80));
        var freshPause = Run(now.AddHours(-10), AgentRunStatus.PausedForApproval, pausedAt: now.AddHours(-1));
        var abandoned = Run(now.AddHours(-80), AgentRunStatus.Running);
        var inFlight = Run(now.AddMinutes(-5), AgentRunStatus.Running);
        var finished = Run(now.AddHours(-80));
        context.AgentWorkflowRuns.AddRange(stalePause, freshPause, abandoned, inFlight, finished);
        await context.SaveChangesAsync();

        var processed = await StaleJob(provider).RunAsync(CancellationToken.None);

        Assert.Equal(2, processed);

        context.ChangeTracker.Clear();

        var timedOutPause = await context.AgentWorkflowRuns.SingleAsync(run => run.Id == stalePause.Id);
        Assert.Equal(AgentRunStatus.TimedOut, timedOutPause.Status);
        Assert.NotNull(timedOutPause.CompletedAt);
        Assert.NotNull(timedOutPause.DurationMs);
        Assert.NotNull(timedOutPause.ApprovalWaitMs);
        Assert.Equal("approval_timeout", timedOutPause.ErrorCode);

        var timedOutRun = await context.AgentWorkflowRuns.SingleAsync(run => run.Id == abandoned.Id);
        Assert.Equal(AgentRunStatus.TimedOut, timedOutRun.Status);
        Assert.NotNull(timedOutRun.CompletedAt);
        Assert.NotNull(timedOutRun.DurationMs);
        // Not an approval wait: nobody was ever asked. An operator reading the row must be able to
        // tell a dead process from an unanswered question.
        Assert.Null(timedOutRun.ApprovalWaitMs);
        Assert.Equal("run_abandoned", timedOutRun.ErrorCode);

        Assert.Equal(AgentRunStatus.PausedForApproval,
            (await context.AgentWorkflowRuns.SingleAsync(run => run.Id == freshPause.Id)).Status);
        // A run minutes old is in flight, not abandoned: the sweep must not time out work that is
        // simply still running.
        Assert.Equal(AgentRunStatus.Running,
            (await context.AgentWorkflowRuns.SingleAsync(run => run.Id == inFlight.Id)).Status);
        Assert.Equal(AgentRunStatus.Succeeded,
            (await context.AgentWorkflowRuns.SingleAsync(run => run.Id == finished.Id)).Status);
    }

    [Fact]
    public async Task StaleJob_IsIdempotent()
    {
        var (provider, context) = BuildProvider();
        var now = DateTime.UtcNow;

        context.AgentWorkflowRuns.Add(
            Run(now.AddHours(-100), AgentRunStatus.PausedForApproval, pausedAt: now.AddHours(-80)));
        await context.SaveChangesAsync();

        var job = StaleJob(provider);
        await job.RunAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var secondRun = await job.RunAsync(CancellationToken.None);

        Assert.Equal(0, secondRun);
    }
}
