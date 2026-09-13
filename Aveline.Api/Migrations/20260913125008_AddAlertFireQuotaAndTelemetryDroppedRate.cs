using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertFireQuotaAndTelemetryDroppedRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FireWindowStart",
                table: "SystemAlertRules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FiresInWindow",
                table: "SystemAlertRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The dropped-sample counter is monotonic and never reset, so Sum over a window
            // is permanently greater than zero after the first drop and the Warning alert
            // latched for ever. Rate measures the per-minute increase instead, which is what
            // eventbus.failed already does (§3.4).
            migrationBuilder.Sql(
                """
                UPDATE "SystemAlertRules" SET "Aggregation" = 'Rate'
                    WHERE "Name" = 'telemetry.dropped';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "SystemAlertRules" SET "Aggregation" = 'Sum'
                    WHERE "Name" = 'telemetry.dropped';
                """);

            migrationBuilder.DropColumn(
                name: "FireWindowStart",
                table: "SystemAlertRules");

            migrationBuilder.DropColumn(
                name: "FiresInWindow",
                table: "SystemAlertRules");
        }
    }
}
