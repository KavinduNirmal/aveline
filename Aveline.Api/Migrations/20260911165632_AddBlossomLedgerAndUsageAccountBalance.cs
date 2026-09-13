using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBlossomLedgerAndUsageAccountBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BlossomAdjusted",
                table: "UsageAccounts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BlossomGranted",
                table: "UsageAccounts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "UsageAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsClosed",
                table: "UsageAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PlanTierSnapshot",
                table: "UsageAccounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "UsageAccounts",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "BlossomLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsageAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BlossomDelta = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BlossomBalanceAfter = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SourceRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SupersedesEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IdempotencyScope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlossomLedgerEntries", x => x.Id);
                    table.CheckConstraint("CK_BlossomLedgerEntries_Delta", "\"BlossomDelta\" <> 0");
                    table.CheckConstraint("CK_BlossomLedgerEntries_Expiry", "\"ExpiresAt\" IS NULL OR \"ExpiresAt\" > \"CreatedAt\"");
                    table.CheckConstraint("CK_BlossomLedgerEntries_Reason", "length(\"Reason\") >= 10");
                    table.ForeignKey(
                        name: "FK_BlossomLedgerEntries_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BlossomLedgerEntries_UsageAccounts_UsageAccountId",
                        column: x => x.UsageAccountId,
                        principalTable: "UsageAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HttpMethod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseStatus = table.Column<short>(type: "smallint", nullable: false),
                    ResponseBodyJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsageAccounts_OrganizationId_IsClosed_PeriodStart",
                table: "UsageAccounts",
                columns: new[] { "OrganizationId", "IsClosed", "PeriodStart" },
                descending: new[] { false, false, true });

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsageAccounts_Balance",
                table: "UsageAccounts",
                sql: "\"BlossomRemaining\" = \"MonthlyBlossomLimit\" + \"BlossomGranted\" - \"BlossomAdjusted\" - \"BlossomUsed\"");

            migrationBuilder.CreateIndex(
                name: "IX_BlossomLedgerEntries_ExpiresAt",
                table: "BlossomLedgerEntries",
                column: "ExpiresAt",
                filter: "\"ExpiresAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BlossomLedgerEntries_OrganizationId_CreatedAt",
                table: "BlossomLedgerEntries",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_BlossomLedgerEntries_OrganizationId_EntryType_CreatedAt",
                table: "BlossomLedgerEntries",
                columns: new[] { "OrganizationId", "EntryType", "CreatedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_BlossomLedgerEntries_OrganizationId_IdempotencyScope_Idempo~",
                table: "BlossomLedgerEntries",
                columns: new[] { "OrganizationId", "IdempotencyScope", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BlossomLedgerEntries_UsageAccountId",
                table: "BlossomLedgerEntries",
                column: "UsageAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAt",
                table: "IdempotencyRecords",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_OrganizationId_Endpoint_IdempotencyKey",
                table: "IdempotencyRecords",
                columns: new[] { "OrganizationId", "Endpoint", "IdempotencyKey" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            // M3 backfill (domain-model.md §10.1). New columns already default to zero /
            // false, so existing rows are valid; correct the tier snapshot from the owning
            // organisation, then synthesise one PeriodAllocation entry per existing period
            // so the ledger and the projection agree from day one. This leaves
            // BlossomRemaining numerically unchanged, which is what makes M3 safe.
            migrationBuilder.Sql(
                "UPDATE \"UsageAccounts\" a " +
                "SET \"PlanTierSnapshot\" = o.\"PlanTier\" " +
                "FROM \"Organizations\" o " +
                "WHERE a.\"OrganizationId\" = o.\"Id\";");

            migrationBuilder.Sql(
                "INSERT INTO \"BlossomLedgerEntries\" " +
                "(\"Id\", \"OrganizationId\", \"UsageAccountId\", \"EntryType\", \"BlossomDelta\", " +
                "\"BlossomBalanceAfter\", \"Reason\", \"SourceKind\", \"CreatedAt\") " +
                "SELECT gen_random_uuid(), a.\"OrganizationId\", a.\"Id\", 'PeriodAllocation', " +
                "a.\"MonthlyBlossomLimit\", " +
                "a.\"MonthlyBlossomLimit\" + a.\"BlossomGranted\" - a.\"BlossomAdjusted\" - a.\"BlossomUsed\", " +
                "'Backfilled from UsageAccounts during migration M3', " +
                "'System', a.\"PeriodStart\" " +
                "FROM \"UsageAccounts\" a " +
                "WHERE a.\"MonthlyBlossomLimit\" <> 0 " +
                "AND NOT EXISTS (SELECT 1 FROM \"BlossomLedgerEntries\" e " +
                "WHERE e.\"UsageAccountId\" = a.\"Id\" AND e.\"EntryType\" = 'PeriodAllocation');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlossomLedgerEntries");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "IX_UsageAccounts_OrganizationId_IsClosed_PeriodStart",
                table: "UsageAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UsageAccounts_Balance",
                table: "UsageAccounts");

            migrationBuilder.DropColumn(
                name: "BlossomAdjusted",
                table: "UsageAccounts");

            migrationBuilder.DropColumn(
                name: "BlossomGranted",
                table: "UsageAccounts");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "UsageAccounts");

            migrationBuilder.DropColumn(
                name: "IsClosed",
                table: "UsageAccounts");

            migrationBuilder.DropColumn(
                name: "PlanTierSnapshot",
                table: "UsageAccounts");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "UsageAccounts");
        }
    }
}
