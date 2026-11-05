using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalByAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "authorization_id",
                schema: "applications",
                table: "approval_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "by_authorization",
                schema: "applications",
                table: "approval_decisions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "authorization_id",
                schema: "applications",
                table: "approval_decisions");

            migrationBuilder.DropColumn(
                name: "by_authorization",
                schema: "applications",
                table: "approval_decisions");
        }
    }
}
