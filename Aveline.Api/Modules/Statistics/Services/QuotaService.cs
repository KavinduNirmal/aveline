using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// Resolves the organisation's plan limits and keeps per-period counters.
/// </summary>
/// <remarks>
/// <para>
/// <b>Safe rollout deviation.</b> Enforcement only happens when
/// <c>Quotas:EnforcementEnabled</c> is <c>true</c> (default <c>false</c>). A limit of
/// <c>0</c> means "not configured" and is unlimited, not an exhausted quota.
/// </para>
/// <para>
/// <b>Fails closed on reconciliation.</b> When the counter store is unreachable and
/// enforcement is enabled, the meter is reported exhausted so a broken counter cannot
/// silently lift a billing limit. When enforcement is disabled the failure is logged and the
/// request always proceeds — the quota path can never fail a request in the default state.
/// </para>
/// </remarks>
public sealed class QuotaService(
    IEntitlementResolver entitlements,
    IQuotaCounterStore counters,
    IEventBus eventBus,
    IOptions<TelemetryOptions> telemetry,
    IOptions<QuotaOptions> quotaOptions,
    ILogger<QuotaService> logger) : IQuotaService
{
    public const string MonthlyMetricKey = "api.requests.monthly";
    public const string PerMinuteMetricKey = "api.requests.perMinute";
    public const string WarningEvent = "apiquota.warning";
    public const string ExhaustedEvent = "apiquota.exhausted";

    private static readonly string[] MetricKeys = [MonthlyMetricKey, PerMinuteMetricKey];

    public bool EnforcementEnabled => quotaOptions.Value.EnforcementEnabled;

    public async Task<QuotaEvaluation> EvaluateAsync(
        Guid organizationId, Guid? apiKeyId, CancellationToken cancellationToken = default)
    {
        var checks = new List<QuotaCheck>();

        foreach (var metricKey in MetricKeys)
        {
            var limit = await ResolveLimitAsync(organizationId, metricKey, cancellationToken);
            if (limit <= 0)
            {
                continue;
            }

            var (periodStart, periodEnd) = ResolvePeriod(metricKey, DateTime.UtcNow);
            var counterKey = CounterKey(organizationId, apiKeyId, metricKey, periodStart);
            var ttl = PeriodTtl(periodEnd, DateTime.UtcNow);

            long used;
            try
            {
                used = await counters.IncrementAsync(counterKey, 1, ttl, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Quota counter unavailable. metricKey={MetricKey} organizationId={OrganizationId}",
                    metricKey, organizationId);

                if (!EnforcementEnabled)
                {
                    // Default rollout: quota can never fail the request.
                    continue;
                }

                // Fail closed (documented): an unverifiable counter must not lift the limit.
                checks.Add(new QuotaCheck(metricKey, limit, limit, periodStart, periodEnd));
                continue;
            }

            var check = new QuotaCheck(metricKey, limit, used, periodStart, periodEnd);
            checks.Add(check);
            await RaiseEventsAsync(organizationId, apiKeyId, check, counterKey, ttl, cancellationToken);
        }

        return new QuotaEvaluation(checks, EnforcementEnabled);
    }

    public async Task<QuotaEvaluation> GetStatusAsync(
        Guid organizationId, Guid? apiKeyId, CancellationToken cancellationToken = default)
    {
        var checks = new List<QuotaCheck>();

        foreach (var metricKey in MetricKeys)
        {
            var limit = await ResolveLimitAsync(organizationId, metricKey, cancellationToken);
            if (limit <= 0)
            {
                continue;
            }

            var (periodStart, periodEnd) = ResolvePeriod(metricKey, DateTime.UtcNow);
            var counterKey = CounterKey(organizationId, apiKeyId, metricKey, periodStart);

            long used;
            try
            {
                used = await counters.GetAsync(counterKey, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Quota counter read failed. metricKey={MetricKey}", metricKey);
                used = 0;
            }

            checks.Add(new QuotaCheck(metricKey, limit, used, periodStart, periodEnd));
        }

        return new QuotaEvaluation(checks, EnforcementEnabled);
    }

    /// <summary>Period boundaries for a metric, in UTC. Month ends are handled by <c>AddMonths</c>.</summary>
    public static (DateTime Start, DateTime End) ResolvePeriod(string metricKey, DateTime now)
    {
        var utc = now.ToUniversalTime();

        if (string.Equals(metricKey, PerMinuteMetricKey, StringComparison.Ordinal))
        {
            var minute = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
            return (minute, minute.AddMinutes(1));
        }

        var month = new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (month, month.AddMonths(1));
    }

    private async Task<long> ResolveLimitAsync(
        Guid organizationId, string metricKey, CancellationToken cancellationToken)
    {
        try
        {
            var value = await entitlements.GetDecimalAsync(
                organizationId, metricKey, fallback: 0m, at: null, cancellationToken);
            return (long)Math.Floor(value);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Entitlement resolution failed. metricKey={MetricKey}", metricKey);
            return 0; // not configured -> unlimited
        }
    }

    private async Task RaiseEventsAsync(
        Guid organizationId,
        Guid? apiKeyId,
        QuotaCheck check,
        string counterKey,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        var warningPercent = telemetry.Value.QuotaWarningPercent;

        if (check.IsExhausted)
        {
            if (await TrySetFlagAsync(counterKey + ":exhausted", ttl, cancellationToken))
            {
                await eventBus.PublishAsync(
                    ExhaustedEvent, organizationId,
                    Payload(organizationId, apiKeyId, check), cancellationToken: cancellationToken);
            }

            return;
        }

        if (check.IsWarning(warningPercent)
            && await TrySetFlagAsync(counterKey + ":warned", ttl, cancellationToken))
        {
            await eventBus.PublishAsync(
                WarningEvent, organizationId,
                Payload(organizationId, apiKeyId, check), cancellationToken: cancellationToken);
        }
    }

    private async Task<bool> TrySetFlagAsync(string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            return await counters.TrySetFlagAsync(key, ttl, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Quota event flag could not be set.");
            return false;
        }
    }

    private static object Payload(Guid organizationId, Guid? apiKeyId, QuotaCheck check) => new
    {
        organizationId,
        apiKeyId,
        metricKey = check.MetricKey,
        limit = check.Limit,
        used = check.Used,
        resetsAt = check.PeriodEnd,
    };

    private static string CounterKey(
        Guid organizationId, Guid? apiKeyId, string metricKey, DateTime periodStart) =>
        $"{organizationId:N}:{apiKeyId?.ToString("N") ?? "org"}:{metricKey}:{periodStart:yyyyMMddHHmm}";

    private static TimeSpan PeriodTtl(DateTime periodEnd, DateTime now)
    {
        var remaining = periodEnd - now.ToUniversalTime();
        return remaining < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : remaining + TimeSpan.FromMinutes(5);
    }
}
