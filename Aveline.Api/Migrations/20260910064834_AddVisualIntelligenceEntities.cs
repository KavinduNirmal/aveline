using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVisualIntelligenceEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Fabric = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Style = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sizes = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    StockQuantity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "available"),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outfit_compositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Occasion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TotalPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    StyleNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outfit_compositions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sourcing_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferenceImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Color = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ItemDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    ProposedMarkup = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    ProposedPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sourcing_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactInfo = table.Column<string>(type: "text", nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ApiEndpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MinimumOrder = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    DeliveryTimeDays = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "customer_matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchConfidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    MatchReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EmployeeActed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_matches_inventory_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "inventory_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DominantColor = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DetectedFabric = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DetectedPattern = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DetectedStyle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ConfidenceScore = table.Column<double>(type: "double precision", nullable: true),
                    AnalysisResult = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_images_inventory_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "inventory_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outfit_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OutfitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outfit_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_outfit_items_inventory_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "inventory_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_outfit_items_outfit_compositions_OutfitId",
                        column: x => x.OutfitId,
                        principalTable: "outfit_compositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_matches_customer",
                table: "customer_matches",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "idx_matches_item",
                table: "customer_matches",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "idx_matches_org",
                table: "customer_matches",
                column: "OrgId");

            migrationBuilder.CreateIndex(
                name: "idx_images_item",
                table: "inventory_images",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "idx_images_org",
                table: "inventory_images",
                column: "OrgId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_images_OrgId_ItemId",
                table: "inventory_images",
                columns: new[] { "OrgId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "idx_inventory_category",
                table: "inventory_items",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "idx_inventory_color",
                table: "inventory_items",
                column: "Color");

            migrationBuilder.CreateIndex(
                name: "idx_inventory_org",
                table: "inventory_items",
                column: "OrgId");

            migrationBuilder.CreateIndex(
                name: "idx_inventory_status",
                table: "inventory_items",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_OrgId_Status_DeletedAt_Category_Color",
                table: "inventory_items",
                columns: new[] { "OrgId", "Status", "DeletedAt", "Category", "Color" });

            migrationBuilder.CreateIndex(
                name: "idx_outfits_org",
                table: "outfit_compositions",
                column: "OrgId");

            migrationBuilder.CreateIndex(
                name: "idx_outfit_items_outfit",
                table: "outfit_items",
                column: "OutfitId");

            migrationBuilder.CreateIndex(
                name: "IX_outfit_items_ItemId",
                table: "outfit_items",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "idx_sourcing_customer",
                table: "sourcing_requests",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "idx_sourcing_org",
                table: "sourcing_requests",
                column: "OrgId");

            migrationBuilder.CreateIndex(
                name: "idx_sourcing_status",
                table: "sourcing_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_suppliers_org",
                table: "suppliers",
                column: "OrgId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_matches");

            migrationBuilder.DropTable(
                name: "inventory_images");

            migrationBuilder.DropTable(
                name: "outfit_items");

            migrationBuilder.DropTable(
                name: "sourcing_requests");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "inventory_items");

            migrationBuilder.DropTable(
                name: "outfit_compositions");
        }
    }
}
