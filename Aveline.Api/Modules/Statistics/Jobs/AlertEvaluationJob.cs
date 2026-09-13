using Aveline.Api.Common.Jobs;
using Aveline.Api.Modules.Statistics.Services;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Statistics.Jobs;

/// <summary>
/// Evaluates every enabled alert rule on a fixed interval
/// (<c>Observability:AlertEvaluationSeconds</c>, default 60) under the distributed job lock
/// (BR-7.5, FR-7.12). Idempotent: cooldown aggregation makes a repeat pass a no-op.
/// </summary>
public sealed class AlertEvaluationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<AlertEvaluationJob> logger,
    IConfiguration configuration)
    : StatisticsJobBase(scopeFactory, jobLock, logger)
{
    private readonly int _evaluationSeconds =
        Math.Max(1, configuration.GetValue("Observability:AlertEvaluationSeconds", 60));

    protected override string JobName => "alert-evaluation";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_evaluationSeconds);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAlertService>();
        return await service.EvaluateAsync(cancellationToken);
    }
}
