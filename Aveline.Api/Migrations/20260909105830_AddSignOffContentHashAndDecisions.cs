using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSignOffContentHashAndDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SignOff_Decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignOff_Decisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignOff_Decisions_MessageId",
                table: "SignOff_Decisions",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SignOff_Decisions_OrganizationId",
                table: "SignOff_Decisions",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignOff_Decisions");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Messages");
        }
    }
}
