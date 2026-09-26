using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSignOffDecisionKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add the kind first, then backfill it from the boolean this migration replaces,
            // and only then drop the boolean. Backfilling after the drop would lose every
            // decision's outcome.
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "SignOffDecisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.Sql(
                """
                UPDATE "SignOffDecisions"
                   SET "Kind" = CASE WHEN "Approved" THEN 'Approved' ELSE 'Rejected' END;
                """);

            migrationBuilder.DropColumn(
                name: "Approved",
                table: "SignOffDecisions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Approved",
                table: "SignOffDecisions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // A revocation has no boolean counterpart; it reads back as not-approved, which is
            // the closest the old shape can express.
            migrationBuilder.Sql(
                """
                UPDATE "SignOffDecisions"
                   SET "Approved" = ("Kind" = 'Approved');
                """);

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "SignOffDecisions");
        }
    }
}
