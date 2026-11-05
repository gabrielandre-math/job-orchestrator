using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Jobs.Data.Migrations
{
    /// <inheritdoc />
    public partial class Eligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "eligibility",
                schema: "jobs",
                table: "postings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "eligibility_evidence",
                schema: "jobs",
                table: "postings",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_postings_eligibility",
                schema: "jobs",
                table: "postings",
                column: "eligibility");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_postings_eligibility",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "eligibility",
                schema: "jobs",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "eligibility_evidence",
                schema: "jobs",
                table: "postings");
        }
    }
}
