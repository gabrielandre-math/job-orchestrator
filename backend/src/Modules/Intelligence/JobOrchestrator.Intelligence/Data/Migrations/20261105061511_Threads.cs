using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Intelligence.Data.Migrations
{
    /// <inheritdoc />
    public partial class Threads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "messages",
                schema: "intelligence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    agent = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    effort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "text", nullable: true),
                    to = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    seconds = table.Column<double>(type: "double precision", nullable: false),
                    ok = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_messages_at",
                schema: "intelligence",
                table: "messages",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_messages_thread_id_at",
                schema: "intelligence",
                table: "messages",
                columns: new[] { "thread_id", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages",
                schema: "intelligence");
        }
    }
}
