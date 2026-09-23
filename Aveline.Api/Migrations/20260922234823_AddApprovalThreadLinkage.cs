using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalThreadLinkage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill before the column becomes NOT NULL (ADR-024, Decision 4).
            //
            // The scaffolder's suggestion was the empty string, which is not a thread id: every
            // legacy row would collide on the unique index created below, and a resume would look up
            // a checkpoint that cannot exist. A per-row value keeps the rows distinct and says
            // plainly that it names no checkpoint - `ConversationId` being null is what marks such a
            // row as never having involved the agent.
            migrationBuilder.Sql(
                """
                UPDATE "ApprovalQueue"
                SET "ThreadId" = 'legacy-' || "Id"::text
                WHERE "ThreadId" IS NULL OR "ThreadId" = '';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ThreadId",
                table: "ApprovalQueue",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalQueue_OrganizationId_ThreadId_Pending",
                table: "ApprovalQueue",
                columns: new[] { "OrganizationId", "ThreadId" },
                unique: true,
                filter: "\"Status\" = 'pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApprovalQueue_OrganizationId_ThreadId_Pending",
                table: "ApprovalQueue");

            migrationBuilder.AlterColumn<string>(
                name: "ThreadId",
                table: "ApprovalQueue",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }
    }
}
