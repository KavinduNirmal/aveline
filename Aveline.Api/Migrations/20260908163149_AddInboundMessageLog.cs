using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundMessageLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboundMessageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    From = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    To = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundMessageLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageLogs_OrganizationId",
                table: "InboundMessageLogs",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageLogs_OrganizationId_ReceivedAt",
                table: "InboundMessageLogs",
                columns: new[] { "OrganizationId", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboundMessageLogs");
        }
    }
}
