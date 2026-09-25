using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogTagsAndItemTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ColorHex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryItemTags",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItemTags", x => new { x.ItemId, x.TagId });
                    table.ForeignKey(
                        name: "FK_InventoryItemTags_CatalogTags_TagId",
                        column: x => x.TagId,
                        principalTable: "CatalogTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryItemTags_InventoryItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_catalog_tags_org_active_order",
                table: "CatalogTags",
                columns: new[] { "OrgId", "IsArchived", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "idx_catalog_tags_org_slug",
                table: "CatalogTags",
                columns: new[] { "OrgId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_inventory_item_tags_org_item",
                table: "InventoryItemTags",
                columns: new[] { "OrgId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "idx_inventory_item_tags_org_tag",
                table: "InventoryItemTags",
                columns: new[] { "OrgId", "TagId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItemTags_TagId",
                table: "InventoryItemTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryItemTags");

            migrationBuilder.DropTable(
                name: "CatalogTags");
        }
    }
}
