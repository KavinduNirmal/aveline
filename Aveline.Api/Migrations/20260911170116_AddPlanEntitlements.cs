using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BillingCycle = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CurrentPeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SeatsIncluded = table.Column<int>(type: "integer", nullable: false),
                    PriceLkr = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CancelAtPeriodEnd = table.Column<bool>(type: "boolean", nullable: false),
                    ExternalProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ExternalSubscriptionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationSubscriptions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanEntitlementOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ValueType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ValueDecimal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ValueBool = table.Column<bool>(type: "boolean", nullable: true),
                    ValueText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanEntitlementOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanEntitlementOverrides_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanEntitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ValueType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ValueDecimal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ValueBool = table.Column<bool>(type: "boolean", nullable: true),
                    ValueText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanEntitlements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSubscriptions_ExternalProvider_ExternalSubscrip~",
                table: "OrganizationSubscriptions",
                columns: new[] { "ExternalProvider", "ExternalSubscriptionId" },
                unique: true,
                filter: "\"ExternalSubscriptionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSubscriptions_OrganizationId",
                table: "OrganizationSubscriptions",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanEntitlementOverrides_OrganizationId_Key_EffectiveFrom",
                table: "PlanEntitlementOverrides",
                columns: new[] { "OrganizationId", "Key", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanEntitlements_PlanTier_Key_EffectiveFrom",
                table: "PlanEntitlements",
                columns: new[] { "PlanTier", "Key", "EffectiveFrom" },
                unique: true);

            // Seed every entitlement row in this migration, so entitlement resolution is
            // authoritative from the moment M4 lands (the values reproduce the hardcoded
            // maps that BR-2.16 removes). See docs/backend/domain-model.md §4.5.
            SeedPlanEntitlements(migrationBuilder);
        }

        private static void SeedPlanEntitlements(MigrationBuilder migrationBuilder)
        {
            var effectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            (string Key, string Type, decimal? Number, bool? Flag, string? Text, decimal Seed, decimal Bloom,
                decimal Orchid, decimal Rose, decimal Enterprise)[] rows =
            [
                ("blossoms.monthly", "Decimal", null, null, null, 150, 750, 2000, 5000, 9999),
                ("staff.max", "Integer", null, null, null, 1, 3, 10, 25, 9999),
                ("customers.active.max", "Integer", null, null, null, 50, 250, 1000, 5000, 999999),
                ("api.requests.monthly", "Integer", null, null, null, 0, 0, 0, 1000000, 10000000),
                ("api.requests.perMinute", "Integer", null, null, null, 30, 60, 300, 600, 6000),
                ("whatsapp.monthly", "Integer", null, null, null, 25, 500, 2000, 5000, 99999),
                ("stats.retentionDays", "Integer", null, null, null, 90, 180, 400, 400, 400),
            ];

            var tiers = new[] { "Seed", "Bloom", "Orchid", "Rose", "Enterprise" };
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                var values = new[] { row.Seed, row.Bloom, row.Orchid, row.Rose, row.Enterprise };
                for (var tierIndex = 0; tierIndex < tiers.Length; tierIndex++)
                {
                    migrationBuilder.InsertData(
                        table: "PlanEntitlements",
                        columns:
                        [
                            "Id", "PlanTier", "Key", "ValueType", "ValueDecimal",
                            "IsEnabled", "EffectiveFrom", "CreatedAt",
                        ],
                        values: new object?[]
                        {
                            Guid.CreateVersion7(), tiers[tierIndex], row.Key, row.Type, values[tierIndex],
                            true, effectiveFrom, effectiveFrom,
                        });
                }
            }

            (string Key, string Seed, string Bloom, string Orchid, string Rose, string Enterprise)[]
                textRows =
            [
                ("agents.visual", "limited", "full", "full", "full", "full"),
                ("agents.commerce", "limited", "limited", "full", "full", "full"),
                ("ai.customContext", "none", "basic", "full", "full", "full"),
                ("analytics.level", "basic", "basic", "advanced", "advanced", "advanced"),
                ("automation.level", "none", "basic", "advanced", "advanced", "advanced"),
            ];

            for (var index = 0; index < textRows.Length; index++)
            {
                var row = textRows[index];
                var values = new[] { row.Seed, row.Bloom, row.Orchid, row.Rose, row.Enterprise };
                for (var tierIndex = 0; tierIndex < tiers.Length; tierIndex++)
                {
                    migrationBuilder.InsertData(
                        table: "PlanEntitlements",
                        columns:
                        [
                            "Id", "PlanTier", "Key", "ValueType", "ValueText",
                            "IsEnabled", "EffectiveFrom", "CreatedAt",
                        ],
                        values: new object?[]
                        {
                            Guid.CreateVersion7(), tiers[tierIndex], row.Key, "String", values[tierIndex],
                            true, effectiveFrom, effectiveFrom,
                        });
                }
            }

            (string Key, bool Seed, bool Bloom, bool Orchid, bool Rose, bool Enterprise)[] boolRows =
            [
                ("api.access", false, false, false, true, true),
                ("ai.customAgents", false, false, false, true, true),
            ];

            for (var index = 0; index < boolRows.Length; index++)
            {
                var row = boolRows[index];
                var values = new[] { row.Seed, row.Bloom, row.Orchid, row.Rose, row.Enterprise };
                for (var tierIndex = 0; tierIndex < tiers.Length; tierIndex++)
                {
                    migrationBuilder.InsertData(
                        table: "PlanEntitlements",
                        columns:
                        [
                            "Id", "PlanTier", "Key", "ValueType", "ValueBool",
                            "IsEnabled", "EffectiveFrom", "CreatedAt",
                        ],
                        values: new object?[]
                        {
                            Guid.CreateVersion7(), tiers[tierIndex], row.Key, "Boolean", values[tierIndex],
                            true, effectiveFrom, effectiveFrom,
                        });
                }
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationSubscriptions");

            migrationBuilder.DropTable(
                name: "PlanEntitlementOverrides");

            migrationBuilder.DropTable(
                name: "PlanEntitlements");
        }
    }
}
