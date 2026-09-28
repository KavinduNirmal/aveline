using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationSubscriptionSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationSubscriptionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDay = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlanTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    HasBillingRow = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SeatsIncluded = table.Column<int>(type: "integer", nullable: false),
                    PriceLkr = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BillingCycle = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsBackfilled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationSubscriptionSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationSubscriptionSnapshots_Organizations_Organizatio~",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgSubscriptionSnapshots_Day_Tier",
                table: "OrganizationSubscriptionSnapshots",
                columns: new[] { "SnapshotDay", "PlanTier" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgSubscriptionSnapshots_Org_Day",
                table: "OrganizationSubscriptionSnapshots",
                columns: new[] { "OrganizationId", "SnapshotDay" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationSubscriptionSnapshots");
        }
    }
}
