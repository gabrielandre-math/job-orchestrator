using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Materials.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "materials");

            migrationBuilder.CreateTable(
                name: "resume_versions",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_version = table.Column<int>(type: "integer", nullable: false),
                    profile_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    written_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    reviewed_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    reviewed = table.Column<bool>(type: "boolean", nullable: false),
                    html = table.Column<string>(type: "text", nullable: false),
                    pdf_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pages = table.Column<int>(type: "integer", nullable: false),
                    provenance = table.Column<string>(type: "jsonb", nullable: false),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resume_versions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_resume_versions_application_id_created_at",
                schema: "materials",
                table: "resume_versions",
                columns: new[] { "application_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resume_versions",
                schema: "materials");
        }
    }
}
