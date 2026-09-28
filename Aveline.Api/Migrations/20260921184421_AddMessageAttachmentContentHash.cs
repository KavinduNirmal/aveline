using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageAttachmentContentHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "MessageAttachments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_OrganizationId_ContentHash",
                table: "MessageAttachments",
                columns: new[] { "OrganizationId", "ContentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MessageAttachments_OrganizationId_ContentHash",
                table: "MessageAttachments");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "MessageAttachments");
        }
    }
}
