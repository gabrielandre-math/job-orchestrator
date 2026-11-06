using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "outcome",
                schema: "applications",
                table: "applications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "outcome_at",
                schema: "applications",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "outcome",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "outcome_at",
                schema: "applications",
                table: "applications");
        }
    }
}
