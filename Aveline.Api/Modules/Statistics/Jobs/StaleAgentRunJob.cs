using Aveline.Api.Common.Jobs;
using Aveline.Api.Modules.Statistics.Repositories;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Statistics.Jobs;

/// <summary>
/// Hourly stale-run sweep (FR-5.10, edge cases "paused run never resumed" and "run abandoned"):
/// a run still <c>PausedForApproval</c> whose <c>PausedAt</c> is older than
/// <c>AgentStats:PausedRunTimeoutHours</c> (default 72), or still <c>Running</c> whose
/// <c>StartedAt</c> is older than <c>AgentStats:RunningRunTimeoutHours</c> (default 1), becomes
/// <c>TimedOut</c> with its <c>CompletedAt</c> and <c>DurationMs</c> set. Idempotent.
/// </summary>
/// <remarks>
/// The <c>Running</c> arm exists because the agent opens a <c>Running</c> row when a run starts
/// (ADR-027). A process that dies mid-run leaves that row behind, and an unreaped one would hold
/// <c>agent.runs_running</c> above zero forever - a permanent false alarm on the one panel whose
/// job is to say whether work is actually in flight.
/// </remarks>
public sealed class StaleAgentRunJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<StaleAgentRunJob> logger)
    : StatisticsJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "stale-agent-run";

    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var pausedTimeoutHours = configuration.GetValue("AgentStats:PausedRunTimeoutHours", 72);
        // An hour, not minutes: a run takes seconds, so anything still `Running` an hour later was
        // abandoned rather than slow - and the sweep is hourly, so finer granularity is moot.
        var runningTimeoutHours = configuration.GetValue("AgentStats:RunningRunTimeoutHours", 1);
        var now = DateTime.UtcNow;

        return await repository.MarkTimedOutAsync(
            pausedBefore: now.AddHours(-pausedTimeoutHours),
            runningStartedBefore: now.AddHours(-runningTimeoutHours),
            completedAt: now,
            cancellationToken);
    }
}
