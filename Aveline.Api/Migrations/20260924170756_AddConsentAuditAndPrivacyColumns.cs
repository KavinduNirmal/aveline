using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddConsentAuditAndPrivacyColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevokeToken",
                table: "CustomerConsent");

            migrationBuilder.AddColumn<string>(
                name: "ConsentSource",
                table: "CustomerConsent",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisclosureShownAt",
                table: "CustomerConsent",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisclosureVersion",
                table: "CustomerConsent",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GlobalSubjectId",
                table: "CustomerConsent",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConsentAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    NewStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ActorKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentAuditEntries_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConsentAuditEntries_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsentAuditEntries_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerConsent_OrganizationId_ConsentStatus",
                table: "CustomerConsent",
                columns: new[] { "OrganizationId", "ConsentStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentAuditEntries_ActorUserId",
                table: "ConsentAuditEntries",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentAuditEntries_CustomerId_CreatedAt",
                table: "ConsentAuditEntries",
                columns: new[] { "CustomerId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentAuditEntries_OrganizationId_CustomerId_CreatedAt",
                table: "ConsentAuditEntries",
                columns: new[] { "OrganizationId", "CustomerId", "CreatedAt" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsentAuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_CustomerConsent_OrganizationId_ConsentStatus",
                table: "CustomerConsent");

            migrationBuilder.DropColumn(
                name: "ConsentSource",
                table: "CustomerConsent");

            migrationBuilder.DropColumn(
                name: "DisclosureShownAt",
                table: "CustomerConsent");

            migrationBuilder.DropColumn(
                name: "DisclosureVersion",
                table: "CustomerConsent");

            migrationBuilder.DropColumn(
                name: "GlobalSubjectId",
                table: "CustomerConsent");

            migrationBuilder.AddColumn<string>(
                name: "RevokeToken",
                table: "CustomerConsent",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }
    }
}
