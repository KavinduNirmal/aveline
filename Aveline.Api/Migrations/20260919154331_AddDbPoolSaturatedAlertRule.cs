using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <summary>
    /// Slice 5 (M-9). The M8 seed inserted a <c>db.pool.saturated</c> rule pointing at
    /// <c>aveline.db.pool_in_use</c>, which nothing produced; the earlier
    /// <c>FixSystemAlertRuleMetricNames</c> migration therefore deleted it (S-37). Npgsql 10 emits
    /// the pool instruments on a meter that is now registered and the collector emits
    /// <c>used / max</c>, so the rule is restored against a metric that has a producer path.
    /// <c>SystemMetricCollectorTests</c> asserts the rule's metric is one the collector emits.
    /// </summary>
    public partial class AddDbPoolSaturatedAlertRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO "SystemAlertRules"
                    ("Id", "Name", "MetricName", "Aggregation", "ComparisonOperator", "Threshold",
                     "WindowSeconds", "Severity", "IsEnabled", "CooldownSeconds", "MaxAlertsPerHour",
                     "TargetRoles", "DimensionFiltersJson", "CreatedByUserId", "CreatedAt", "UpdatedAt",
                     "LastTriggeredAt")
                VALUES
                    ('b0f9a0f5-6d5e-4a1f-9f2b-1f4a0c9e7d21', 'db.pool.saturated', 'aveline.db.pool.saturation', 'Max', 'Gt', 0.9, 300, 'Warning', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL)
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "SystemAlertRules" WHERE "Name" = 'db.pool.saturated';
                """);
        }
    }
}
