using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Services;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Statistics.Jobs;

/// <summary>
/// Hourly quota reconciliation (FR-6.6/BR-6.6): writes the live counter value into the
/// durable <c>ApiQuotaUsage</c> row for every organisation with recent rollup activity, so a
/// Redis loss does not erase period usage. Idempotent — the row is upserted by scope,
/// metric and period.
/// </summary>
public sealed class ApiQuotaResetJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<ApiQuotaResetJob> logger)
    : StatisticsJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "api-quota-reset";

    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quota = scope.ServiceProvider.GetRequiredService<IQuotaService>();

        var since = DateTime.UtcNow.AddHours(-2);
        var organizationIds = await db.ApiRequestMetrics.AsNoTracking()
            .Where(metric => metric.OrganizationId != null && metric.WindowStart >= since)
            .Select(metric => metric.OrganizationId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var written = 0;
        foreach (var organizationId in organizationIds)
        {
            var status = await quota.GetStatusAsync(organizationId, apiKeyId: null, cancellationToken);
            foreach (var check in status.Checks)
            {
                var existing = await db.ApiQuotaUsage.FirstOrDefaultAsync(
                    usage => usage.OrganizationId == organizationId
                             && usage.ApiKeyId == null
                             && usage.MetricKey == check.MetricKey
                             && usage.PeriodStart == check.PeriodStart,
                    cancellationToken);

                if (existing is null)
                {
                    db.ApiQuotaUsage.Add(new ApiQuotaUsage
                    {
                        OrganizationId = organizationId,
                        ApiKeyId = null,
                        MetricKey = check.MetricKey,
                        PeriodStart = check.PeriodStart,
                        PeriodEnd = check.PeriodEnd,
                        LimitValue = check.Limit,
                        UsedValue = check.Used,
                        UpdatedAt = DateTime.UtcNow,
                    });
                }
                else
                {
                    existing.PeriodEnd = check.PeriodEnd;
                    existing.LimitValue = check.Limit;
                    existing.UsedValue = check.Used;
                    existing.UpdatedAt = DateTime.UtcNow;
                }

                written++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return written;
    }
}
