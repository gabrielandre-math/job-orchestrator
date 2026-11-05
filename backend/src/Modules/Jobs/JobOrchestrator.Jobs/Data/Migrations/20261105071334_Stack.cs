using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Jobs.Data.Migrations
{
    /// <inheritdoc />
    public partial class Stack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "company_domain",
                schema: "jobs",
                table: "postings",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "enriched",
                schema: "jobs",
                table: "postings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<List<string>>(
                name: "stack",
                schema: "jobs",
                table: "postings",
                type: "text[]",
                nullable: false,
                defaultValueSql: "\'{}\'::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "company_domain",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "enriched",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "stack",
                schema: "jobs",
                table: "postings");
        }
    }
}
