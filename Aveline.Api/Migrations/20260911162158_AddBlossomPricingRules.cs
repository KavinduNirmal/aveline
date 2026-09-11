using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBlossomPricingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // btree_gist is required for the GiST exclusion constraint below (BR-1.3).
            // EF Core cannot express an exclusion constraint, so this migration is
            // hand-extended exactly as docs/backend/domain-model.md §3.1 specifies.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            migrationBuilder.CreateTable(
                name: "BlossomConversionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UnitsPerBlossom = table.Column<int>(type: "integer", nullable: false),
                    MinimumChargeBlossoms = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RoundingMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RoundingDecimals = table.Column<short>(type: "smallint", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ChangeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlossomConversionRules", x => x.Id);
                    table.CheckConstraint("CK_BlossomConversionRules_Decimals", "\"RoundingDecimals\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_BlossomConversionRules_Minimum", "\"MinimumChargeBlossoms\" >= 0");
                    table.CheckConstraint("CK_BlossomConversionRules_Range", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" > \"EffectiveFrom\"");
                    table.CheckConstraint("CK_BlossomConversionRules_Scope", "(\"ScopeKind\" = 'Global' AND \"Provider\" IS NULL AND \"Model\" IS NULL) OR (\"ScopeKind\" = 'Provider' AND \"Provider\" IS NOT NULL AND \"Model\" IS NULL) OR (\"ScopeKind\" = 'ProviderModel' AND \"Provider\" IS NOT NULL AND \"Model\" IS NOT NULL)");
                    table.CheckConstraint("CK_BlossomConversionRules_Units", "\"UnitsPerBlossom\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "BlossomPriceEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SkuKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SkuCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BlossomQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PriceLkr = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ChangeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlossomPriceEntries", x => x.Id);
                    table.CheckConstraint("CK_BlossomPriceEntries_Price", "\"PriceLkr\" >= 0");
                    table.CheckConstraint("CK_BlossomPriceEntries_Quantity", "\"BlossomQuantity\" > 0");
                    table.CheckConstraint("CK_BlossomPriceEntries_Range", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" > \"EffectiveFrom\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlossomConversionRules_ScopeKind_Provider_Model_EffectiveFr~",
                table: "BlossomConversionRules",
                columns: new[] { "ScopeKind", "Provider", "Model", "EffectiveFrom" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_BlossomConversionRules_Status_EffectiveFrom_EffectiveTo",
                table: "BlossomConversionRules",
                columns: new[] { "Status", "EffectiveFrom", "EffectiveTo" },
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_BlossomPriceEntries_PlanTier_OrganizationId_SkuKind_SkuCode~",
                table: "BlossomPriceEntries",
                columns: new[] { "PlanTier", "OrganizationId", "SkuKind", "SkuCode", "EffectiveFrom" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            // Database-enforced non-overlap per scope. An application-only check races
            // under concurrent admin writes, so the invariant lives in PostgreSQL.
            migrationBuilder.Sql(
                "ALTER TABLE \"BlossomConversionRules\" " +
                "ADD CONSTRAINT \"EX_BlossomConversionRules_NoOverlap\" " +
                "EXCLUDE USING gist (" +
                "\"ScopeKind\" WITH =, " +
                "coalesce(\"Provider\", '') WITH =, " +
                "coalesce(\"Model\", '') WITH =, " +
                "tstzrange(\"EffectiveFrom\", \"EffectiveTo\", '[)') WITH &&" +
                ") WHERE (\"Status\" IN ('Draft', 'Active'));");

            // Backs the coalesce expressions used by the exclusion constraint and the
            // scope lookup, so the constraint is maintained through an index.
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_BlossomConversionRules_ExScope\" " +
                "ON \"BlossomConversionRules\" " +
                "(\"ScopeKind\", coalesce(\"Provider\", ''), coalesce(\"Model\", ''));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlossomConversionRules");

            migrationBuilder.DropTable(
                name: "BlossomPriceEntries");
        }
    }
}
