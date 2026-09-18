using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixSystemAlertRuleMetricNames : Migration
    {
        // The M8 seed wrote short metric names that no producer emits (C-5). The seed class
        // alone cannot update rows already inserted by M8, so this migration rewrites those
        // rows by Name to the aveline.* names SystemMetricCollector now produces, and removes
        // the db.pool.saturated rule because the pool gauges are not instrumented (S-37).
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "SystemAlertRules" SET "MetricName" = 'aveline.blossom.balance'
                    WHERE "Name" = 'blossom.balance.negative';
                UPDATE "SystemAlertRules" SET "MetricName" = 'aveline.blossom.reconciliation.drift'
                    WHERE "Name" = 'blossom.ledger.drift';
                UPDATE "SystemAlertRules" SET "MetricName" = 'aveline.blossom.consumed_rate'
                    WHERE "Name" = 'blossom.runaway.org';
                UPDATE "SystemAlertRules" SET "MetricName" = 'aveline.api.error_rate'
                    WHERE "Name" = 'api.error.rate';
                UPDATE "SystemAlertRules" SET "MetricName" = 'aveline.api.latency_p95'
                    WHERE "Name" = 'api.latency.p95';
                UPDATE "SystemAlertRules" SET "MetricName" = 'aveline.agent.success_rate'
                    WHERE "Name" = 'agent.failure.rate';
                UPDATE "SystemAlertRules" SET "MetricName" = 'aveline.agent.paused_count'
                    WHERE "Name" = 'agent.run.stuck';
                UPDATE "SystemAlertRules" SET "MetricName" = 'aveline.agent.steps_per_run'
                    WHERE "Name" = 'agent.step.runaway';

                DELETE FROM "SystemAlertRules" WHERE "Name" = 'db.pool.saturated';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "SystemAlertRules" SET "MetricName" = 'blossom.balance'
                    WHERE "Name" = 'blossom.balance.negative';
                UPDATE "SystemAlertRules" SET "MetricName" = 'blossom.reconciliation.drift'
                    WHERE "Name" = 'blossom.ledger.drift';
                UPDATE "SystemAlertRules" SET "MetricName" = 'blossom.consumed.rate'
                    WHERE "Name" = 'blossom.runaway.org';
                UPDATE "SystemAlertRules" SET "MetricName" = 'api.error_rate'
                    WHERE "Name" = 'api.error.rate';
                UPDATE "SystemAlertRules" SET "MetricName" = 'api.latency.p95'
                    WHERE "Name" = 'api.latency.p95';
                UPDATE "SystemAlertRules" SET "MetricName" = 'agent.success_rate'
                    WHERE "Name" = 'agent.failure.rate';
                UPDATE "SystemAlertRules" SET "MetricName" = 'agent.paused.count'
                    WHERE "Name" = 'agent.run.stuck';
                UPDATE "SystemAlertRules" SET "MetricName" = 'agent.steps.per_run'
                    WHERE "Name" = 'agent.step.runaway';

                INSERT INTO "SystemAlertRules"
                    ("Id", "Name", "MetricName", "Aggregation", "ComparisonOperator", "Threshold",
                     "WindowSeconds", "Severity", "IsEnabled", "CooldownSeconds", "MaxAlertsPerHour",
                     "TargetRoles", "DimensionFiltersJson", "CreatedByUserId", "CreatedAt", "UpdatedAt",
                     "LastTriggeredAt")
                VALUES
                    ('028eb8fa-6f86-4a6c-ad90-d53138615ba5', 'db.pool.saturated', 'aveline.db.pool_in_use', 'Avg', 'Gt', 90, 300, 'Critical', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL)
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }
    }
}
