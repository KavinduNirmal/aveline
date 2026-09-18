using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationAndUserFks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedBy",
                table: "Orders",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRecords_OrganizationId",
                table: "NotificationRecords",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Approval_Queue_DecidedBy",
                table: "Approval_Queue",
                column: "DecidedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_AiUsageRecords_Organizations_OrganizationId",
                table: "AiUsageRecords",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Approval_Queue_Users_DecidedBy",
                table: "Approval_Queue",
                column: "DecidedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IntegrationCredentials_Organizations_OrganizationId",
                table: "IntegrationCredentials",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationRecords_Organizations_OrganizationId",
                table: "NotificationRecords",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Users_CreatedBy",
                table: "Orders",
                column: "CreatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UsageAccounts_Organizations_OrganizationId",
                table: "UsageAccounts",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiUsageRecords_Organizations_OrganizationId",
                table: "AiUsageRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_Approval_Queue_Users_DecidedBy",
                table: "Approval_Queue");

            migrationBuilder.DropForeignKey(
                name: "FK_IntegrationCredentials_Organizations_OrganizationId",
                table: "IntegrationCredentials");

            migrationBuilder.DropForeignKey(
                name: "FK_NotificationRecords_Organizations_OrganizationId",
                table: "NotificationRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Users_CreatedBy",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_UsageAccounts_Organizations_OrganizationId",
                table: "UsageAccounts");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CreatedBy",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_NotificationRecords_OrganizationId",
                table: "NotificationRecords");

            migrationBuilder.DropIndex(
                name: "IX_Approval_Queue_DecidedBy",
                table: "Approval_Queue");
        }
    }
}
