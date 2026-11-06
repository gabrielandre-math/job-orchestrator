using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Jobs.Data.Migrations
{
    /// <inheritdoc />
    public partial class HistoricalPostings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "history",
                schema: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ats = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    board = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    remote_scope = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    arrangement = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    eligibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    eligibility_evidence = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    snapshot_open = table.Column<bool>(type: "boolean", nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    checked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    imported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_history", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_history_snapshot_ats_board_external_id",
                schema: "jobs",
                table: "history",
                columns: new[] { "snapshot", "ats", "board", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_history_status_source",
                schema: "jobs",
                table: "history",
                columns: new[] { "status", "source" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "history",
                schema: "jobs");
        }
    }
}
