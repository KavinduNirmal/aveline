using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationOwnerUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Conversations_OrganizationId_CustomerId_Kind",
                table: "Conversations");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "Conversations",
                type: "uuid",
                nullable: true);

            // Before ADR-021 the general Salon was organization-wide (OwnerUserId null). It is the
            // owner's concierge thread, so bind the existing row to the organization owner to
            // preserve its history. Customer-bound Salons and inbound channel Salons (which carry
            // an ExternalRef) stay organization-shared with a null owner.
            migrationBuilder.Sql(
                """
                UPDATE "Conversations" AS c
                SET "OwnerUserId" = o."OwnerUserId"
                FROM "Organizations" AS o
                WHERE c."OrganizationId" = o."Id"
                  AND c."Kind" = 'Salon'
                  AND c."CustomerId" IS NULL
                  AND c."ExternalRef" IS NULL
                  AND c."OwnerUserId" IS NULL
                  AND o."OwnerUserId" <> '00000000-0000-0000-0000-000000000000'::uuid;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_OrganizationId_OwnerUserId_CustomerId_Kind",
                table: "Conversations",
                columns: new[] { "OrganizationId", "OwnerUserId", "CustomerId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_OwnerUserId",
                table: "Conversations",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Users_OwnerUserId",
                table: "Conversations",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Users_OwnerUserId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_OrganizationId_OwnerUserId_CustomerId_Kind",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_OwnerUserId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Conversations");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_OrganizationId_CustomerId_Kind",
                table: "Conversations",
                columns: new[] { "OrganizationId", "CustomerId", "Kind" });
        }
    }
}
