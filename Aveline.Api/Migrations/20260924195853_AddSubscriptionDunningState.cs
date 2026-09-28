using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionDunningState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DunningStartedAt",
                table: "OrganizationSubscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRenewalAttemptAt",
                table: "OrganizationSubscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RenewalAttemptCount",
                table: "OrganizationSubscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DunningStartedAt",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "NextRenewalAttemptAt",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "RenewalAttemptCount",
                table: "OrganizationSubscriptions");
        }
    }
}
