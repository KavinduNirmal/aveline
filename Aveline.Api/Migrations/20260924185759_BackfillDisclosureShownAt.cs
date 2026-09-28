using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <summary>
    /// A data-only migration (privacy plan §4.2/§4.4). Phase 0 already added the
    /// <c>DisclosureShownAt</c>/<c>DisclosureVersion</c> columns, so this migration changes no
    /// schema: it backfills <c>DisclosureShownAt = CreatedAt</c> for every consent row that existed
    /// before the disclosure feature shipped.
    /// <para>
    /// Without the backfill, every historical customer would look like a first contact on their next
    /// inbound message and would be mass-messaged on deploy. Stamping them marks "this row predates
    /// the disclosure" rather than claiming a notice was actually shown, which is why
    /// <c>DisclosureVersion</c> is deliberately left NULL for the backfilled rows.
    /// </para>
    /// <para>
    /// Idempotent by construction: the <c>WHERE ... IS NULL</c> predicate means re-running it, or
    /// running it after a real disclosure has been recorded, changes nothing.
    /// </para>
    /// </summary>
    public partial class BackfillDisclosureShownAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A single set-based UPDATE. Written as raw SQL on purpose: this is a data step, not a
            // model change, so there is no EF operation that expresses it.
            migrationBuilder.Sql(
                """
                UPDATE "CustomerConsent"
                SET "DisclosureShownAt" = "CreatedAt"
                WHERE "DisclosureShownAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately not reversible. Setting the stamps back to NULL would re-arm the
            // mass-message hazard this migration exists to prevent, and there is no way to tell a
            // backfilled stamp from a real one after the fact. Reverting the data step is never the
            // right response to a bad deploy; rolling forward is.
        }
    }
}
