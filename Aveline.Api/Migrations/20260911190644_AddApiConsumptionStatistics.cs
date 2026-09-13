using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApiConsumptionStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // =====================================================================
            // HAND-EDITED MIGRATION — the only one permitted (domain-model.md §10.2).
            // Reason: EF Core cannot express PostgreSQL declarative range partitioning,
            // so the generated "ApiRequestLogs" CREATE TABLE is replaced here with a
            // PARTITION BY RANGE table, the two partition-management functions are
            // created, the current and next day partitions are materialised, and the two
            // partial OccurredAt indexes (which EF cannot model because it identifies an
            // index by its property set) are created with raw SQL.
            // =====================================================================
            migrationBuilder.Sql(
                """
                CREATE TABLE "ApiRequestLogs" (
                    "Id" bigint GENERATED ALWAYS AS IDENTITY,
                    "OccurredAt" timestamp with time zone NOT NULL,
                    "OrganizationId" uuid NULL,
                    "ApiKeyId" uuid NULL,
                    "UserId" uuid NULL,
                    "RouteTemplate" character varying(200) NOT NULL,
                    "HttpMethod" character varying(10) NOT NULL,
                    "StatusCode" smallint NOT NULL,
                    "DurationMs" integer NOT NULL,
                    "RequestBytes" integer NOT NULL,
                    "ResponseBytes" integer NOT NULL,
                    "RequestId" character varying(128) NULL,
                    "TraceId" uuid NULL,
                    "ClientIpHash" character(64) NULL,
                    "UserAgentHash" character(64) NULL,
                    "ErrorCode" character varying(64) NULL,
                    "ResourceType" character varying(64) NULL,
                    "ResourceId" character varying(128) NULL,
                    CONSTRAINT "PK_ApiRequestLogs" PRIMARY KEY ("Id", "OccurredAt")
                ) PARTITION BY RANGE ("OccurredAt");
                """);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION aveline_ensure_api_request_log_partition(p_day date)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    v_name text := format('ApiRequestLogs_%s', to_char(p_day, 'YYYYMMDD'));
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE c.relname = v_name AND n.nspname = current_schema()
                    ) THEN
                        EXECUTE format(
                            'CREATE TABLE %I PARTITION OF "ApiRequestLogs" FOR VALUES FROM (%L) TO (%L)',
                            v_name, p_day, p_day + 1);
                    END IF;
                END;
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION aveline_drop_old_api_request_log_partitions(p_retention_days int)
                RETURNS integer
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    v_cutoff date := (now() AT TIME ZONE 'utc')::date - p_retention_days;
                    v_name text;
                    v_dropped integer := 0;
                BEGIN
                    FOR v_name IN
                        SELECT c.relname
                        FROM pg_class c
                        JOIN pg_inherits i ON i.inhrelid = c.oid
                        JOIN pg_class p ON p.oid = i.inhparent
                        WHERE p.relname = 'ApiRequestLogs'
                          AND c.relname ~ '^ApiRequestLogs_[0-9]{8}$'
                          AND to_date(right(c.relname, 8), 'YYYYMMDD') < v_cutoff
                    LOOP
                        EXECUTE format('DROP TABLE %I', v_name);
                        v_dropped := v_dropped + 1;
                    END LOOP;
                    RETURN v_dropped;
                END;
                $$;
                """);

            migrationBuilder.Sql(
                """
                SELECT aveline_ensure_api_request_log_partition((now() AT TIME ZONE 'utc')::date);
                SELECT aveline_ensure_api_request_log_partition((now() AT TIME ZONE 'utc')::date + 1);
                """);

            migrationBuilder.Sql(
                "CREATE INDEX \"IX_ApiRequestLogs_Slow\" ON \"ApiRequestLogs\" (\"OccurredAt\" DESC) WHERE \"DurationMs\" > 1000;");
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_ApiRequestLogs_Errors\" ON \"ApiRequestLogs\" (\"OccurredAt\" DESC) WHERE \"StatusCode\" >= 500;");

            migrationBuilder.CreateTable(
                name: "ApiQuotaUsage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetricKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LimitValue = table.Column<long>(type: "bigint", nullable: true),
                    UsedValue = table.Column<long>(type: "bigint", nullable: false),
                    WarnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExhaustedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiQuotaUsage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiRequestMetrics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RouteTemplate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HttpMethod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    StatusCode = table.Column<short>(type: "smallint", nullable: false),
                    StatusClass = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    IsThrottled = table.Column<bool>(type: "boolean", nullable: false),
                    WindowStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowSize = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    RequestCount = table.Column<long>(type: "bigint", nullable: false),
                    ErrorCount = table.Column<long>(type: "bigint", nullable: false),
                    TotalDurationMs = table.Column<long>(type: "bigint", nullable: false),
                    MaxDurationMs = table.Column<int>(type: "integer", nullable: false),
                    BucketCounts = table.Column<int[]>(type: "integer[]", nullable: false),
                    RequestBytes = table.Column<long>(type: "bigint", nullable: false),
                    ResponseBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiRequestMetrics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiQuotaUsage_Scope_Metric_Period",
                table: "ApiQuotaUsage",
                columns: new[] { "OrganizationId", "ApiKeyId", "MetricKey", "PeriodStart" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestLogs_Key_Occurred",
                table: "ApiRequestLogs",
                columns: new[] { "ApiKeyId", "OccurredAt" },
                descending: new[] { false, true },
                filter: "\"ApiKeyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestLogs_Org_Occurred",
                table: "ApiRequestLogs",
                columns: new[] { "OrganizationId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestLogs_RequestId",
                table: "ApiRequestLogs",
                column: "RequestId",
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestMetrics_Dimensions",
                table: "ApiRequestMetrics",
                columns: new[] { "OrganizationId", "ApiKeyId", "UserId", "RouteTemplate", "HttpMethod", "StatusCode", "WindowStart" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestMetrics_Key_Window",
                table: "ApiRequestMetrics",
                columns: new[] { "ApiKeyId", "WindowStart" },
                descending: new[] { false, true },
                filter: "\"ApiKeyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestMetrics_Org_Window",
                table: "ApiRequestMetrics",
                columns: new[] { "OrganizationId", "WindowStart" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestMetrics_Route_Window",
                table: "ApiRequestMetrics",
                columns: new[] { "RouteTemplate", "WindowStart" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestMetrics_Window",
                table: "ApiRequestMetrics",
                column: "WindowStart");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Hand-edited counterpart to Up: the partition-management functions are not
            // modelled by EF, so they must be dropped explicitly before the table.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS aveline_drop_old_api_request_log_partitions(int);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS aveline_ensure_api_request_log_partition(date);");

            migrationBuilder.DropTable(
                name: "ApiQuotaUsage");

            migrationBuilder.DropTable(
                name: "ApiRequestLogs");

            migrationBuilder.DropTable(
                name: "ApiRequestMetrics");
        }
    }
}
