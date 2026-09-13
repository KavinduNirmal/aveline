using Aveline.Api.Authorization;

namespace Aveline.Api.Modules.Statistics.Models;

/// <summary>
/// The twelve seeded alert rules from implementation-plan.md §7.4, with stable GUIDs so the
/// migration is idempotent and tests can reference a rule by id. Thresholds are the
/// documented ones; a rule whose condition has no numeric threshold (the "5× org baseline"
/// runaway rule) stores the multiplier.
/// </summary>
/// <remarks>
/// The operator set has no <c>Ne</c> (domain-model.md §8.2), so the <c>blossom.ledger.drift</c>
/// rule is encoded as <c>Max &gt; 0</c> and the collector records the drift metric as a
/// magnitude; any non-zero drift therefore breaches it. This is documented in
/// docs/backend/README.md.
/// </remarks>
public static class SystemAlertRuleSeed
{
    /// <summary>Stable timestamp so the seed is deterministic across migrations and tests.</summary>
    public static readonly DateTime CreatedAt = new(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);

    public static readonly IReadOnlyList<SystemAlertRule> Rules =
    [
        Rule(
            "4ffa1139-f6b0-4a65-b292-7f2fe4fc789b", "blossom.balance.negative", "blossom.balance",
            AlertAggregation.Min, AlertComparisonOperator.Lt, 0m, 300, AlertSeverity.Critical),
        Rule(
            "5a8632d2-648e-4af7-a7e3-15cb9251f9d1", "blossom.ledger.drift", "blossom.reconciliation.drift",
            AlertAggregation.Max, AlertComparisonOperator.Gt, 0m, 300, AlertSeverity.Critical),
        Rule(
            "0d42af72-faaf-4abe-a6d0-40d4f13c9d19", "blossom.runaway.org", "blossom.consumed.rate",
            AlertAggregation.Avg, AlertComparisonOperator.Gt, 5m, 900, AlertSeverity.Warning),
        Rule(
            "569e2d89-12d1-4799-aefc-8a37fa64b7dd", "api.error.rate", "api.error_rate",
            AlertAggregation.Avg, AlertComparisonOperator.Gt, 0.05m, 600, AlertSeverity.Critical),
        Rule(
            "4930e701-85de-4da3-911e-3bd685d3e8ec", "api.latency.p95", "api.latency.p95",
            AlertAggregation.Avg, AlertComparisonOperator.Gt, 2000m, 600, AlertSeverity.Warning),
        Rule(
            "f6e6046e-129e-4466-9fcc-a238e001b634", "agent.failure.rate", "agent.success_rate",
            AlertAggregation.Avg, AlertComparisonOperator.Lt, 0.8m, 900, AlertSeverity.Warning),
        Rule(
            "66afae34-8601-4873-90e7-97fbc71627e6", "agent.run.stuck", "agent.paused.count",
            AlertAggregation.Max, AlertComparisonOperator.Gt, 10m, 3600, AlertSeverity.Warning),
        Rule(
            "a9dde35c-3b46-45a5-b1b1-90dc35bc3593", "agent.step.runaway", "agent.steps.per_run",
            AlertAggregation.Max, AlertComparisonOperator.Gt, 200m, 60, AlertSeverity.Warning),
        Rule(
            "f259bd52-2aff-4152-93b4-f3a79e864b44", "telemetry.dropped", "aveline.api.telemetry.dropped",
            AlertAggregation.Sum, AlertComparisonOperator.Gt, 0m, 300, AlertSeverity.Warning),
        Rule(
            "028eb8fa-6f86-4a6c-ad90-d53138615ba5", "db.pool.saturated", "aveline.db.pool_in_use",
            AlertAggregation.Avg, AlertComparisonOperator.Gt, 90m, 300, AlertSeverity.Critical),
        Rule(
            "98d1f73f-95a8-4c44-a275-091c6c9b661e", "queue.telemetry.backlog", "aveline.queue.telemetry_channel",
            AlertAggregation.Max, AlertComparisonOperator.Gt, 8000m, 300, AlertSeverity.Warning),
        Rule(
            "44bbc6e3-5c24-40d7-aad8-f4c089640f30", "eventbus.failed", "aveline.eventbus.failed",
            AlertAggregation.Rate, AlertComparisonOperator.Gt, 10m, 60, AlertSeverity.Critical),
    ];

    private static SystemAlertRule Rule(
        string id, string name, string metricName, AlertAggregation aggregation,
        AlertComparisonOperator comparison, decimal threshold, int windowSeconds, AlertSeverity severity) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        MetricName = metricName,
        Aggregation = aggregation,
        ComparisonOperator = comparison,
        Threshold = threshold,
        WindowSeconds = windowSeconds,
        Severity = severity,
        IsEnabled = true,
        CooldownSeconds = 300,
        MaxAlertsPerHour = 10,
        TargetRoles = [Roles.BoutiqueOwner, Roles.BoutiqueManager],
        CreatedByUserId = Guid.Empty,
        CreatedAt = CreatedAt,
        UpdatedAt = CreatedAt,
    };
}
