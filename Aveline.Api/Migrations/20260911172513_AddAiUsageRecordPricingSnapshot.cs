using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageRecordPricingSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "NormalizedUnits",
                table: "AiUsageRecords",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PricingRuleId",
                table: "AiUsageRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PricingRuleVersion",
                table: "AiUsageRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "RoundingDecimals",
                table: "AiUsageRecords",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoundingMode",
                table: "AiUsageRecords",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnitsPerBlossom",
                table: "AiUsageRecords",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NormalizedUnits",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "PricingRuleId",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "PricingRuleVersion",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "RoundingDecimals",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "RoundingMode",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "UnitsPerBlossom",
                table: "AiUsageRecords");
        }
    }
}
