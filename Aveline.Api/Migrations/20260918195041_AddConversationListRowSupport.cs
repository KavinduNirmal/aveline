using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationListRowSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Conversations_OrganizationId_LastMessageAt_Id",
                table: "Conversations",
                columns: new[] { "OrganizationId", "LastMessageAt", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Conversations_OrganizationId_LastMessageAt_Id",
                table: "Conversations");
        }
    }
}
