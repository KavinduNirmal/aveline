using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(Aveline.Api.Infrastructure.Data.AppDbContext))]
    [Migration("20260917204500_AddContentTypeToInventoryImage")]
    /// <inheritdoc />
    public partial class AddContentTypeToInventoryImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "InventoryImages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "image/jpeg");

            migrationBuilder.AddColumn<byte[]>(
                name: "ImageData",
                table: "InventoryImages",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "InventoryImages",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "InventoryImages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ItemId",
                table: "InventoryImages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "InventoryImages");

            migrationBuilder.DropColumn(
                name: "ImageData",
                table: "InventoryImages");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "InventoryImages");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "InventoryImages");

            migrationBuilder.AlterColumn<Guid>(
                name: "ItemId",
                table: "InventoryImages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
