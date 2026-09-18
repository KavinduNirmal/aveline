using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyRollupTablesAndWindowSizeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApiRequestMetrics_Dimensions",
                table: "ApiRequestMetrics");

            migrationBuilder.CreateTable(
                name: "DailyAgentMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgentKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Day = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RunCount = table.Column<int>(type: "integer", nullable: false),
                    SucceededCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    PausedCount = table.Column<int>(type: "integer", nullable: false),
                    TimedOutCount = table.Column<int>(type: "integer", nullable: false),
                    CancelledCount = table.Column<int>(type: "integer", nullable: false),
                    TotalDurationMs = table.Column<long>(type: "bigint", nullable: false),
                    MaxDurationMs = table.Column<int>(type: "integer", nullable: false),
                    AvgDurationMs = table.Column<double>(type: "double precision", nullable: true),
                    P50DurationMs = table.Column<double>(type: "double precision", nullable: true),
                    P95DurationMs = table.Column<double>(type: "double precision", nullable: true),
                    P99DurationMs = table.Column<double>(type: "double precision", nullable: true),
                    StepCount = table.Column<int>(type: "integer", nullable: false),
                    ToolCallCount = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CachedTokens = table.Column<long>(type: "bigint", nullable: false),
                    ActualCostUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    BlossomUnits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyAgentMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyBillingMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlanTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestCount = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CachedTokens = table.Column<long>(type: "bigint", nullable: false),
                    ActualCostUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    BlossomUnits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyBillingMetrics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageLogs_OrganizationId_ExternalId",
                table: "InboundMessageLogs",
                columns: new[] { "OrganizationId", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestMetrics_Dimensions",
                table: "ApiRequestMetrics",
                columns: new[] { "OrganizationId", "ApiKeyId", "UserId", "RouteTemplate", "HttpMethod", "StatusCode", "WindowStart", "WindowSize" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_PricingRuleId",
                table: "AiUsageRecords",
                column: "PricingRuleId",
                filter: "\"PricingRuleId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DailyAgentMetrics_Org_Agent_Day",
                table: "DailyAgentMetrics",
                columns: new[] { "OrganizationId", "AgentKey", "Day" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_DailyBillingMetrics_Org_Day_Provider_Model",
                table: "DailyBillingMetrics",
                columns: new[] { "OrganizationId", "Day", "Provider", "Model" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyAgentMetrics");

            migrationBuilder.DropTable(
                name: "DailyBillingMetrics");

            migrationBuilder.DropIndex(
                name: "IX_InboundMessageLogs_OrganizationId_ExternalId",
                table: "InboundMessageLogs");

            migrationBuilder.DropIndex(
                name: "IX_ApiRequestMetrics_Dimensions",
                table: "ApiRequestMetrics");

            migrationBuilder.DropIndex(
                name: "IX_AiUsageRecords_PricingRuleId",
                table: "AiUsageRecords");

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestMetrics_Dimensions",
                table: "ApiRequestMetrics",
                columns: new[] { "OrganizationId", "ApiKeyId", "UserId", "RouteTemplate", "HttpMethod", "StatusCode", "WindowStart" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
