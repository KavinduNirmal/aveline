using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerMemorySearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The lexical leg's column is deliberately NOT part of the EF model: the in-memory test
            // provider cannot map PostgreSQL's `tsvector`, and mapping it would break model
            // validation for the whole in-memory suite (ADR-017 precedent). It is created here via
            // raw SQL and searched through raw SQL in CustomerMemoryRepository (ADR-025).
            //
            //   * SearchVector - a weighted generated tsvector, the same shape the handbook uses.
            //     The weights make a statement about a garment outrank an incidental category match,
            //     which is what an exact-token query ("Kanjeevaram", "bridal lehenga") needs. The
            //     configuration is written as `'english'::regconfig` because the two-argument
            //     to_tsvector is immutable only with a constant configuration, which a generated
            //     column requires.
            //
            // The GIN index is PARTIAL on live rows, unlike the handbook's. Every search leg filters
            // `DeletedAt IS NULL` (a withdrawn note must never be retrieved), and rows are only ever
            // soft-deleted except by a GDPR erase, so indexing withdrawn rows would be dead weight
            // the planner never reads. The predicate is also the same definition of "present" the
            // existing partial unique index on (OrganizationId, CustomerId, ContentKey) uses.
            migrationBuilder.Sql(
                """
                ALTER TABLE "CustomerMemory" ADD COLUMN "SearchVector" tsvector
                    GENERATED ALWAYS AS (
                        setweight(to_tsvector('english'::regconfig, coalesce("Content", '')), 'A') ||
                        setweight(to_tsvector('english'::regconfig, coalesce("Category", '')), 'B')
                    ) STORED;
                CREATE INDEX "IX_CustomerMemory_SearchVector"
                    ON "CustomerMemory" USING gin ("SearchVector")
                    WHERE "DeletedAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_CustomerMemory_SearchVector";
                ALTER TABLE "CustomerMemory" DROP COLUMN IF EXISTS "SearchVector";
                """);
        }
    }
}
