using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Automation.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "automation");

            migrationBuilder.CreateTable(
                name: "adapter_stats",
                schema: "automation",
                columns: table => new
                {
                    adapter_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    domain = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    capability = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    confirmed = table.Column<int>(type: "integer", nullable: false),
                    failures = table.Column<int>(type: "integer", nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    circuit_open_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_failure_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_failure_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adapter_stats", x => new { x.adapter_id, x.domain, x.capability });
                });

            migrationBuilder.CreateTable(
                name: "routing_records",
                schema: "automation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    capability = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    chosen_adapter = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    route = table.Column<string>(type: "jsonb", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routing_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_routing_records_application_id",
                schema: "automation",
                table: "routing_records",
                column: "application_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adapter_stats",
                schema: "automation");

            migrationBuilder.DropTable(
                name: "routing_records",
                schema: "automation");
        }
    }
}
