using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Jobs.Data.Migrations
{
    /// <inheritdoc />
    public partial class Normalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "apply_channel",
                schema: "jobs",
                table: "postings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "apply_email",
                schema: "jobs",
                table: "postings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contract",
                schema: "jobs",
                table: "postings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "english",
                schema: "jobs",
                table: "postings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "modality",
                schema: "jobs",
                table: "postings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "salary_currency",
                schema: "jobs",
                table: "postings",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "salary_max",
                schema: "jobs",
                table: "postings",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "salary_min",
                schema: "jobs",
                table: "postings",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "salary_text",
                schema: "jobs",
                table: "postings",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seniority",
                schema: "jobs",
                table: "postings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sponsorship",
                schema: "jobs",
                table: "postings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "apply_channel",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "apply_email",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "contract",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "english",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "modality",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "salary_currency",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "salary_max",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "salary_min",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "salary_text",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "seniority",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "sponsorship",
                schema: "jobs",
                table: "postings");
        }
    }
}
