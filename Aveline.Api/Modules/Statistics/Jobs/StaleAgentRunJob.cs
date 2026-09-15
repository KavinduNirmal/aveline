using Aveline.Api.Common.Jobs;
using Aveline.Api.Modules.Statistics.Repositories;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Statistics.Jobs;

/// <summary>
/// Hourly stale-run sweep (FR-5.10, edge case "paused run never resumed"): a run still
/// <c>PausedForApproval</c> whose <c>PausedAt</c> is older than
/// <c>AgentStats:PausedRunTimeoutHours</c> (default 72) becomes <c>TimedOut</c> with its
/// <c>CompletedAt</c> and <c>DurationMs</c> set. Idempotent.
/// </summary>
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

        var timeoutHours = configuration.GetValue("AgentStats:PausedRunTimeoutHours", 72);
        var now = DateTime.UtcNow;

        return await repository.MarkTimedOutAsync(
            now.AddHours(-timeoutHours), completedAt: now, cancellationToken);
    }
}
