using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentIntentsAndProviderEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderIntentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PriceLkr = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SkuCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BlossomQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    PlanTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    BillingPeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BillingPeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderSubscriptionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SettledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentIntents", x => x.Id);
                    table.CheckConstraint("CK_PaymentIntents_Amount", "\"AmountMinor\" > 0");
                    table.CheckConstraint("CK_PaymentIntents_Settled", "(\"Status\" <> 'Succeeded' OR \"SettledAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_PaymentIntents_TopUpShape", "(\"Purpose\" <> 'BlossomTopUp' OR (\"SkuCode\" IS NOT NULL AND \"BlossomQuantity\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_PaymentIntents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentProviderEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderIntentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentProviderEvents", x => x.Id);
                    table.CheckConstraint("CK_PaymentProviderEvents_Amount", "(\"AmountMinor\" IS NULL OR \"AmountMinor\" >= 0)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_Org",
                table: "PaymentIntents",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_Org_Purpose_IdempotencyKey",
                table: "PaymentIntents",
                columns: new[] { "OrganizationId", "Purpose", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_Provider_IntentId",
                table: "PaymentIntents",
                columns: new[] { "Provider", "ProviderIntentId" },
                unique: true,
                filter: "\"ProviderIntentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderEvents_Provider_EventId",
                table: "PaymentProviderEvents",
                columns: new[] { "Provider", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderEvents_ProviderIntentId",
                table: "PaymentProviderEvents",
                column: "ProviderIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderEvents_Unprocessed",
                table: "PaymentProviderEvents",
                column: "ReceivedAt",
                filter: "\"ProcessedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentIntents");

            migrationBuilder.DropTable(
                name: "PaymentProviderEvents");
        }
    }
}
