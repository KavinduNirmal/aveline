using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerConciergeEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "new"),
                    TotalSpent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    VisitCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastVisitAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customers_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customer_Consent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsentStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "pending"),
                    ConsentGrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsentRevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokeToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer_Consent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customer_Consent_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Customer_Consent_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customer_Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "other"),
                    EventDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ReminderSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customer_Events_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Customer_Events_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customer_Interactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "whatsapp"),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "inbound"),
                    MessageContent = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ParsedIntentJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer_Interactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customer_Interactions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Customer_Interactions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customer_Interactions_Users_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customer_Memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "fact"),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "conversation"),
                    IsExplicit = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Confidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false, defaultValue: 0.50m),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer_Memory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customer_Memory_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Customer_Memory_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // The pgvector `embedding vector(1536)` column and its HNSW cosine index are not part
            // of the EF model (the in-memory test provider cannot map the pgvector type), so they
            // are created here via raw SQL. Requires the `vector` extension (docker-compose image
            // ships pgvector). See ADR-017.
            migrationBuilder.Sql(
                """
                ALTER TABLE "Customer_Memory" ADD COLUMN embedding vector(1536);
                CREATE INDEX "IX_Customer_Memory_Embedding"
                    ON "Customer_Memory" USING hnsw (embedding vector_cosine_ops);
                """);

            migrationBuilder.CreateTable(
                name: "Customer_Preferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferenceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreferenceValue = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsExplicit = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Confidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false, defaultValue: 0.50m),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "conversation"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer_Preferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customer_Preferences_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Customer_Preferences_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customer_Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer_Tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customer_Tags_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Customer_Tags_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Consent_CustomerId",
                table: "Customer_Consent",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Consent_OrganizationId_CustomerId",
                table: "Customer_Consent",
                columns: new[] { "OrganizationId", "CustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Events_CustomerId",
                table: "Customer_Events",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Events_EventDate",
                table: "Customer_Events",
                column: "EventDate");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Events_OrganizationId",
                table: "Customer_Events",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Interactions_Channel",
                table: "Customer_Interactions",
                column: "Channel");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Interactions_CustomerId",
                table: "Customer_Interactions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Interactions_CustomerId_CreatedAt",
                table: "Customer_Interactions",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Interactions_OrganizationId",
                table: "Customer_Interactions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Interactions_StaffMemberId",
                table: "Customer_Interactions",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Memory_Category",
                table: "Customer_Memory",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Memory_CustomerId",
                table: "Customer_Memory",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Memory_OrganizationId",
                table: "Customer_Memory",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Preferences_CustomerId",
                table: "Customer_Preferences",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Preferences_CustomerId_PreferenceKey",
                table: "Customer_Preferences",
                columns: new[] { "CustomerId", "PreferenceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Preferences_OrganizationId",
                table: "Customer_Preferences",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Tags_CustomerId",
                table: "Customer_Tags",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Tags_CustomerId_Tag",
                table: "Customer_Tags",
                columns: new[] { "CustomerId", "Tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Tags_OrganizationId",
                table: "Customer_Tags",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CreatedBy",
                table: "Customers",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_OrganizationId",
                table: "Customers",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_OrganizationId_PhoneNumber",
                table: "Customers",
                columns: new[] { "OrganizationId", "PhoneNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Status",
                table: "Customers",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_Customer_Memory_Embedding";
                ALTER TABLE "Customer_Memory" DROP COLUMN IF EXISTS embedding;
                """);

            migrationBuilder.DropTable(
                name: "Customer_Consent");

            migrationBuilder.DropTable(
                name: "Customer_Events");

            migrationBuilder.DropTable(
                name: "Customer_Interactions");

            migrationBuilder.DropTable(
                name: "Customer_Memory");

            migrationBuilder.DropTable(
                name: "Customer_Preferences");

            migrationBuilder.DropTable(
                name: "Customer_Tags");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
