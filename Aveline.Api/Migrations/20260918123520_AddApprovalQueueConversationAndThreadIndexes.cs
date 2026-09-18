using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalQueueConversationAndThreadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ApprovalQueue_ConversationId",
                table: "ApprovalQueue",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalQueue_ThreadId",
                table: "ApprovalQueue",
                column: "ThreadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApprovalQueue_ConversationId",
                table: "ApprovalQueue");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalQueue_ThreadId",
                table: "ApprovalQueue");
        }
    }
}
