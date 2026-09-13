using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyHttpMethodToUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRecords_OrganizationId_Endpoint_IdempotencyKey",
                table: "IdempotencyRecords");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_Org_Endpoint_Method_Key",
                table: "IdempotencyRecords",
                columns: new[] { "OrganizationId", "Endpoint", "HttpMethod", "IdempotencyKey" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRecords_Org_Endpoint_Method_Key",
                table: "IdempotencyRecords");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_OrganizationId_Endpoint_IdempotencyKey",
                table: "IdempotencyRecords",
                columns: new[] { "OrganizationId", "Endpoint", "IdempotencyKey" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
