using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorApiKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TraceId = table.Column<Guid>(type: "uuid", nullable: true),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_Action_CreatedAt",
                table: "AuditLogEntries",
                columns: new[] { "Action", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_ActorUserId_CreatedAt",
                table: "AuditLogEntries",
                columns: new[] { "ActorUserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_EntityType_EntityId_CreatedAt",
                table: "AuditLogEntries",
                columns: new[] { "EntityType", "EntityId", "CreatedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_OrganizationId_CreatedAt",
                table: "AuditLogEntries",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogEntries");
        }
    }
}
