using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgenticStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgentWorkflowRunId",
                table: "AiUsageRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentWorkflowRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkflowId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ParentWorkflowRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TraceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TriggerKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TriggerRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    InitiatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AgentsInvolved = table.Column<List<string>>(type: "text[]", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    PausedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovalWaitMs = table.Column<int>(type: "integer", nullable: true),
                    StepCount = table.Column<int>(type: "integer", nullable: false),
                    ToolCallCount = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CachedTokens = table.Column<int>(type: "integer", nullable: false),
                    NormalizedUnits = table.Column<long>(type: "bigint", nullable: true),
                    ActualCostUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    BlossomUnits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PricingRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanTierAtRun = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsUnattributed = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentWorkflowRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentWorkflowRuns_AgentWorkflowRuns_ParentWorkflowRunId",
                        column: x => x.ParentWorkflowRunId,
                        principalTable: "AgentWorkflowRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentWorkflowRuns_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentStepRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    StepIndex = table.Column<short>(type: "smallint", nullable: false),
                    AgentKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NodeName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StepKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ToolName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttemptNumber = table.Column<short>(type: "smallint", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CachedTokens = table.Column<int>(type: "integer", nullable: false),
                    ActualCostUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    ArgsHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    ResultBytes = table.Column<int>(type: "integer", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentStepRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentStepRuns_AgentWorkflowRuns_WorkflowRunId",
                        column: x => x.WorkflowRunId,
                        principalTable: "AgentWorkflowRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_AgentWorkflowRunId",
                table: "AiUsageRecords",
                column: "AgentWorkflowRunId",
                unique: true,
                filter: "\"AgentWorkflowRunId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentStepRuns_OrganizationId_AgentKey_StartedAt",
                table: "AgentStepRuns",
                columns: new[] { "OrganizationId", "AgentKey", "StartedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AgentStepRuns_OrganizationId_StartedAt",
                table: "AgentStepRuns",
                columns: new[] { "OrganizationId", "StartedAt" },
                descending: new[] { false, true },
                filter: "\"Status\" = 'Failed'");

            migrationBuilder.CreateIndex(
                name: "IX_AgentStepRuns_ToolName_StartedAt",
                table: "AgentStepRuns",
                columns: new[] { "ToolName", "StartedAt" },
                descending: new[] { false, true },
                filter: "\"ToolName\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentStepRuns_WorkflowRunId_StepIndex_AttemptNumber",
                table: "AgentStepRuns",
                columns: new[] { "WorkflowRunId", "StepIndex", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentWorkflowRuns_AgentsInvolved",
                table: "AgentWorkflowRuns",
                column: "AgentsInvolved")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_AgentWorkflowRuns_OrganizationId_StartedAt",
                table: "AgentWorkflowRuns",
                columns: new[] { "OrganizationId", "StartedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AgentWorkflowRuns_OrganizationId_TriggerKind_StartedAt",
                table: "AgentWorkflowRuns",
                columns: new[] { "OrganizationId", "TriggerKind", "StartedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AgentWorkflowRuns_OrganizationId_WorkflowId",
                table: "AgentWorkflowRuns",
                columns: new[] { "OrganizationId", "WorkflowId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_AgentWorkflowRuns_ParentWorkflowRunId",
                table: "AgentWorkflowRuns",
                column: "ParentWorkflowRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentWorkflowRuns_StartedAt",
                table: "AgentWorkflowRuns",
                column: "StartedAt",
                descending: new bool[0],
                filter: "\"IsUnattributed\"");

            migrationBuilder.CreateIndex(
                name: "IX_AgentWorkflowRuns_Status_StartedAt",
                table: "AgentWorkflowRuns",
                columns: new[] { "Status", "StartedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_AiUsageRecords_AgentWorkflowRuns_AgentWorkflowRunId",
                table: "AiUsageRecords",
                column: "AgentWorkflowRunId",
                principalTable: "AgentWorkflowRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiUsageRecords_AgentWorkflowRuns_AgentWorkflowRunId",
                table: "AiUsageRecords");

            migrationBuilder.DropTable(
                name: "AgentStepRuns");

            migrationBuilder.DropTable(
                name: "AgentWorkflowRuns");

            migrationBuilder.DropIndex(
                name: "IX_AiUsageRecords_AgentWorkflowRunId",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "AgentWorkflowRunId",
                table: "AiUsageRecords");
        }
    }
}
