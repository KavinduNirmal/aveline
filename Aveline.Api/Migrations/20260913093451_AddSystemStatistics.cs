using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemAlertRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MetricName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Aggregation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ComparisonOperator = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    WindowSeconds = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxAlertsPerHour = table.Column<int>(type: "integer", nullable: false),
                    TargetRoles = table.Column<string>(type: "jsonb", nullable: false),
                    DimensionFiltersJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastTriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemAlertRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemMetricSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    MetricName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DimensionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    DimensionHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ValueDecimal = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    ValueBigint = table.Column<long>(type: "bigint", nullable: true),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    WindowStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowSize = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SampledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemMetricSamples", x => x.Id);
                    table.CheckConstraint("CK_SystemMetricSamples_Value", "(\"ValueDecimal\" IS NOT NULL) <> (\"ValueBigint\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "SystemAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetricName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ObservedValue = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Threshold = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    OccurrenceCount = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveOkCount = table.Column<int>(type: "integer", nullable: false),
                    FiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NotificationRecordId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemAlerts_SystemAlertRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "SystemAlertRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlertRules_Enabled",
                table: "SystemAlertRules",
                column: "IsEnabled",
                filter: "\"IsEnabled\"");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlertRules_Name",
                table: "SystemAlertRules",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_Org_Fired",
                table: "SystemAlerts",
                columns: new[] { "OrganizationId", "FiredAt" },
                descending: new[] { false, true },
                filter: "\"OrganizationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_RuleId",
                table: "SystemAlerts",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_Severity_Status",
                table: "SystemAlerts",
                columns: new[] { "Severity", "Status", "FiredAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_Status_Fired",
                table: "SystemAlerts",
                columns: new[] { "Status", "FiredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SystemMetricSamples_Metric_Dims_Window",
                table: "SystemMetricSamples",
                columns: new[] { "MetricName", "DimensionHash", "WindowStart", "WindowSize" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemMetricSamples_Metric_Window",
                table: "SystemMetricSamples",
                columns: new[] { "MetricName", "WindowStart" },
                descending: new[] { false, true });

            // The twelve seeded alert rules (implementation-plan.md §7.4) with stable GUIDs.
            // TargetRoles is canonicalised the same way as SystemAlertRuleSeed.TargetRoles;
            // "owner or admin" maps to the organisation owner and manager boutique roles,
            // because IRecipientResolver filters on membership.BoutiqueRole (BR-7.11). The
            // operator set has no Ne, so blossom.ledger.drift is Max > 0 over a magnitude
            // drift metric (see the seed class and docs/backend/README.md).
            migrationBuilder.Sql(
                """
                INSERT INTO "SystemAlertRules"
                    ("Id", "Name", "MetricName", "Aggregation", "ComparisonOperator", "Threshold",
                     "WindowSeconds", "Severity", "IsEnabled", "CooldownSeconds", "MaxAlertsPerHour",
                     "TargetRoles", "DimensionFiltersJson", "CreatedByUserId", "CreatedAt", "UpdatedAt",
                     "LastTriggeredAt")
                VALUES
                    ('4ffa1139-f6b0-4a65-b292-7f2fe4fc789b', 'blossom.balance.negative', 'blossom.balance', 'Min', 'Lt', 0, 300, 'Critical', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL),
                    ('5a8632d2-648e-4af7-a7e3-15cb9251f9d1', 'blossom.ledger.drift', 'blossom.reconciliation.drift', 'Max', 'Gt', 0, 300, 'Critical', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL),
                    ('0d42af72-faaf-4abe-a6d0-40d4f13c9d19', 'blossom.runaway.org', 'blossom.consumed.rate', 'Avg', 'Gt', 5, 900, 'Warning', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL),
                    ('569e2d89-12d1-4799-aefc-8a37fa64b7dd', 'api.error.rate', 'api.error_rate', 'Avg', 'Gt', 0.05, 600, 'Critical', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL),
                    ('4930e701-85de-4da3-911e-3bd685d3e8ec', 'api.latency.p95', 'api.latency.p95', 'Avg', 'Gt', 2000, 600, 'Warning', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL),
                    ('f6e6046e-129e-4466-9fcc-a238e001b634', 'agent.failure.rate', 'agent.success_rate', 'Avg', 'Lt', 0.8, 900, 'Warning', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL),
                    ('66afae34-8601-4873-90e7-97fbc71627e6', 'agent.run.stuck', 'agent.paused.count', 'Max', 'Gt', 10, 3600, 'Warning', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL),
                    ('a9dde35c-3b46-45a5-b1b1-90dc35bc3593', 'agent.step.runaway', 'agent.steps.per_run', 'Max', 'Gt', 200, 60, 'Warning', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL),
                    ('f259bd52-2aff-4152-93b4-f3a79e864b44', 'telemetry.dropped', 'aveline.api.telemetry.dropped', 'Sum', 'Gt', 0, 300, 'Warning', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL),
                    ('028eb8fa-6f86-4a6c-ad90-d53138615ba5', 'db.pool.saturated', 'aveline.db.pool_in_use', 'Avg', 'Gt', 90, 300, 'Critical', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL),
                    ('98d1f73f-95a8-4c44-a275-091c6c9b661e', 'queue.telemetry.backlog', 'aveline.queue.telemetry_channel', 'Max', 'Gt', 8000, 300, 'Warning', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL),
                    ('44bbc6e3-5c24-40d7-aad8-f4c089640f30', 'eventbus.failed', 'aveline.eventbus.failed', 'Rate', 'Gt', 10, 60, 'Critical', true, 300, 10, '["org:boutique_owner","org:boutique_manager"]', NULL, '00000000-0000-0000-0000-000000000000', '2026-09-11T00:00:00Z', '2026-09-11T00:00:00Z', NULL)
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemAlerts");

            migrationBuilder.DropTable(
                name: "SystemMetricSamples");

            migrationBuilder.DropTable(
                name: "SystemAlertRules");
        }
    }
}
