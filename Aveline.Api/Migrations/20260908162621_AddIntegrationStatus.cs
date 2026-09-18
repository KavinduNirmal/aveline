using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastConnectedAt",
                table: "IntegrationCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "IntegrationCredentials",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "IntegrationCredentials",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Pending");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastConnectedAt",
                table: "IntegrationCredentials");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "IntegrationCredentials");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "IntegrationCredentials");
        }
    }
}
