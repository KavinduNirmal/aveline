using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHandbookChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HandbookChunk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "company"),
                    SourceTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    HeadingPath = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Anchor = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Audience = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "staff"),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    TagsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandbookChunk", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HandbookChunk_Audience",
                table: "HandbookChunk",
                column: "Audience");

            migrationBuilder.CreateIndex(
                name: "IX_HandbookChunk_ContentHash",
                table: "HandbookChunk",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_HandbookChunk_SourceKey_Ordinal",
                table: "HandbookChunk",
                columns: new[] { "SourceKey", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HandbookChunk_SourceKind",
                table: "HandbookChunk",
                column: "SourceKind");

            // The two search columns are deliberately NOT part of the EF model: the in-memory test
            // provider cannot map pgvector's `vector` or PostgreSQL's `tsvector`, and mapping them
            // would break model validation for the whole in-memory suite (ADR-017 precedent).
            // They are created here via raw SQL and searched through raw SQL in
            // HandbookRepository (ADR-025).
            //
            //   * embedding    - the dense leg: pgvector cosine with an HNSW index.
            //   * SearchVector - the lexical leg: a weighted generated tsvector with a GIN index.
            //     The A/B/C weights make a title or heading match outrank an incidental body
            //     match, which is what a label-shaped query ("Code Expiration") needs. The
            //     configuration is written as `'english'::regconfig` because the two-argument
            //     to_tsvector is immutable only with a constant configuration, which a generated
            //     column requires.
            migrationBuilder.Sql(
                """
                CREATE EXTENSION IF NOT EXISTS vector;
                ALTER TABLE "HandbookChunk" ADD COLUMN embedding vector(1536);
                CREATE INDEX "IX_HandbookChunk_Embedding"
                    ON "HandbookChunk" USING hnsw (embedding vector_cosine_ops);

                ALTER TABLE "HandbookChunk" ADD COLUMN "SearchVector" tsvector
                    GENERATED ALWAYS AS (
                        setweight(to_tsvector('english'::regconfig, coalesce("SourceTitle", '')), 'A') ||
                        setweight(to_tsvector('english'::regconfig, coalesce("HeadingPath", '')), 'B') ||
                        setweight(to_tsvector('english'::regconfig, coalesce("Content", '')), 'C')
                    ) STORED;
                CREATE INDEX "IX_HandbookChunk_SearchVector"
                    ON "HandbookChunk" USING gin ("SearchVector");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_HandbookChunk_SearchVector";
                ALTER TABLE "HandbookChunk" DROP COLUMN IF EXISTS "SearchVector";
                DROP INDEX IF EXISTS "IX_HandbookChunk_Embedding";
                ALTER TABLE "HandbookChunk" DROP COLUMN IF EXISTS embedding;
                """);

            migrationBuilder.DropTable(
                name: "HandbookChunk");
        }
    }
}
