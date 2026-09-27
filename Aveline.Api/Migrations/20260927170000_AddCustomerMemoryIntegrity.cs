using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <summary>
    /// The memory write-path integrity work (customer-memory gap analysis, A1/A3/A4) plus the
    /// customer description column the pre-contact brief reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three changes that belong together because they are all about a memory being a real,
    /// addressable record rather than an append-only log:
    /// <list type="bullet">
    /// <item><c>ContentKey</c> - the normalised statement, so "the same note" can be enforced by
    /// the database instead of hoped for by the reader (A3).</item>
    /// <item><c>ExpiresAt</c> - so a dated note can age out without being deleted (A4).</item>
    /// <item>a partial unique index over <c>(OrganizationId, CustomerId, ContentKey)</c> where the
    /// row is live - the actual constraint, which a read-then-insert cannot provide (A3).</item>
    /// </list>
    /// </para>
    /// <para>
    /// The backfill is not optional. The column is non-nullable and the unique index is created
    /// after it, so existing rows must first be given their key and any statements already stored
    /// more than once must be collapsed - otherwise the index creation would fail on a database
    /// that has been running, which is exactly the database this migration will meet.
    /// </para>
    /// </remarks>
    public partial class AddCustomerMemoryIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Customers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentKey",
                table: "CustomerMemory",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExpiresAt",
                table: "CustomerMemory",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill the key for rows written before the column existed. The expression mirrors
            // MemoryContentKey.From: lower-case, strip trailing sentence punctuation and collapse
            // whitespace runs. A divergence here only produces a different key, never a wrong row,
            // because the moment the unique index exists the application recomputes the key on the
            // next write of that statement.
            migrationBuilder.Sql(
                """
                UPDATE "CustomerMemory"
                SET "ContentKey" = btrim(
                        regexp_replace(
                            lower(btrim(btrim("Content"), '.!?')),
                            '\s+', ' ', 'g'),
                        ' ')
                WHERE "ContentKey" = '';
                """);

            // Collapse statements that were already stored more than once. The newest row survives
            // and the older ones are soft-deleted: the tombstone is the honest record (the note did
            // exist) and it keeps them out of the partial unique index, so no data is destroyed.
            // Runs before the index, so the index cannot meet rows the rule now forbids.
            migrationBuilder.Sql(
                """
                UPDATE "CustomerMemory" AS older
                SET "DeletedAt" = now(), "UpdatedAt" = now()
                FROM "CustomerMemory" AS newer
                WHERE older."OrganizationId" = newer."OrganizationId"
                  AND older."CustomerId" = newer."CustomerId"
                  AND older."ContentKey" = newer."ContentKey"
                  AND older."DeletedAt" IS NULL
                  AND newer."DeletedAt" IS NULL
                  AND (older."CreatedAt", older."Id") < (newer."CreatedAt", newer."Id");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMemory_OrganizationId_CustomerId_ContentKey",
                table: "CustomerMemory",
                columns: new[] { "OrganizationId", "CustomerId", "ContentKey" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMemory_OrganizationId_CustomerId_ExpiresAt",
                table: "CustomerMemory",
                columns: new[] { "OrganizationId", "CustomerId", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerMemory_OrganizationId_CustomerId_ContentKey",
                table: "CustomerMemory");

            migrationBuilder.DropIndex(
                name: "IX_CustomerMemory_OrganizationId_CustomerId_ExpiresAt",
                table: "CustomerMemory");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "ContentKey",
                table: "CustomerMemory");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "CustomerMemory");
        }
    }
}
