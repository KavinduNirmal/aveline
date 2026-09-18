using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aveline.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalThreadLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                table: "Approval_Queue",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThreadId",
                table: "Approval_Queue",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "Approval_Queue");

            migrationBuilder.DropColumn(
                name: "ThreadId",
                table: "Approval_Queue");
        }
    }
}
