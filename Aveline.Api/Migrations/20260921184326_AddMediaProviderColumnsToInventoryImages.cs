using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaProviderColumnsToInventoryImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "InventoryImages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageProvider",
                table: "InventoryImages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "database");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "InventoryImages");

            migrationBuilder.DropColumn(
                name: "StorageProvider",
                table: "InventoryImages");
        }
    }
}
