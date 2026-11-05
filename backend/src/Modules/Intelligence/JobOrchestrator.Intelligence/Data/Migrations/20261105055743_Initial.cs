using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Intelligence.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "intelligence");

            migrationBuilder.CreateTable(
                name: "cache",
                schema: "intelligence",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    task = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    json = table.Column<string>(type: "text", nullable: false),
                    provider_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    effort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cache", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "calls",
                schema: "intelligence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    task = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    provider_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    effort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ok = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    seconds = table.Column<double>(type: "double precision", nullable: false),
                    degraded = table.Column<bool>(type: "boolean", nullable: false),
                    cached = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calls", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cache_expires_at",
                schema: "intelligence",
                table: "cache",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_calls_task_at",
                schema: "intelligence",
                table: "calls",
                columns: new[] { "task", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cache",
                schema: "intelligence");

            migrationBuilder.DropTable(
                name: "calls",
                schema: "intelligence");
        }
    }
}
