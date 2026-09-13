using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOverrideNoOverlapConstraintAndPartialIdempotencyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRecords_Org_Endpoint_Method_Key",
                table: "IdempotencyRecords");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_Org_Endpoint_Method_Key",
                table: "IdempotencyRecords",
                columns: new[] { "OrganizationId", "Endpoint", "HttpMethod", "IdempotencyKey" },
                unique: true,
                filter: "\"OrganizationId\" IS NOT NULL");

            // Database-enforced non-overlap for one key's override windows. The unique index
            // on (OrganizationId, Key, EffectiveFrom) cannot catch two different starts whose
            // half-open ranges overlap, so two concurrent PATCHes could both pass the
            // application check and commit an ambiguous pair (§2.4). btree_gist (installed by
            // migration M2) makes the uuid/text equality operators GiST-indexable.
            migrationBuilder.Sql(
                "ALTER TABLE \"PlanEntitlementOverrides\" " +
                "ADD CONSTRAINT \"EX_PlanEntitlementOverrides_NoOverlap\" " +
                "EXCLUDE USING gist (" +
                "\"OrganizationId\" WITH =, " +
                "\"Key\" WITH =, " +
                "tstzrange(\"EffectiveFrom\", \"EffectiveTo\", '[)') WITH &&" +
                ");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"PlanEntitlementOverrides\" " +
                "DROP CONSTRAINT \"EX_PlanEntitlementOverrides_NoOverlap\";");

            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRecords_Org_Endpoint_Method_Key",
                table: "IdempotencyRecords");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_Org_Endpoint_Method_Key",
                table: "IdempotencyRecords",
                columns: new[] { "OrganizationId", "Endpoint", "HttpMethod", "IdempotencyKey" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
