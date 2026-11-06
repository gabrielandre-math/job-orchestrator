using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "archived_at",
                schema: "applications",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "archived_at",
                schema: "applications",
                table: "applications");
        }
    }
}
