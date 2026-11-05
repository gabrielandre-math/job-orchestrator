using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Mail.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "mail");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "mail",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    smtp_host = table.Column<string>(type: "text", nullable: false),
                    smtp_port = table.Column<int>(type: "integer", nullable: false),
                    smtp_ssl = table.Column<bool>(type: "boolean", nullable: false),
                    imap_host = table.Column<string>(type: "text", nullable: false),
                    imap_port = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    checked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    last_uid = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "incoming",
                schema: "mail",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<string>(type: "text", nullable: false),
                    from = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    snippet = table.Column<string>(type: "text", nullable: false),
                    @class = table.Column<string>(name: "class", type: "text", nullable: true),
                    action_needed = table.Column<bool>(type: "boolean", nullable: false),
                    outgoing_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incoming", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outgoing",
                schema: "mail",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company = table.Column<string>(type: "text", nullable: false),
                    job_title = table.Column<string>(type: "text", nullable: false),
                    to = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    follows_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    message_id = table.Column<string>(type: "text", nullable: true),
                    attachment_path = table.Column<string>(type: "text", nullable: true),
                    attachment_name = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    drafted_by = table.Column<string>(type: "text", nullable: false),
                    check_notes = table.Column<string>(type: "text", nullable: true),
                    replied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reply_class = table.Column<string>(type: "text", nullable: true),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outgoing", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "mail",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    daily_cap = table.Column<int>(type: "integer", nullable: false),
                    follow_up_days = table.Column<int>(type: "integer", nullable: false),
                    window_start = table.Column<int>(type: "integer", nullable: false),
                    window_end = table.Column<int>(type: "integer", nullable: false),
                    attach_resume = table.Column<bool>(type: "boolean", nullable: false),
                    auto_consent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    auto_consent_text = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "templates",
                schema: "mail",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_incoming_message_id",
                schema: "mail",
                table: "incoming",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_job_id_kind",
                schema: "mail",
                table: "outgoing",
                columns: new[] { "job_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_message_id",
                schema: "mail",
                table: "outgoing",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_templates_kind_language",
                schema: "mail",
                table: "templates",
                columns: new[] { "kind", "language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts",
                schema: "mail");

            migrationBuilder.DropTable(
                name: "incoming",
                schema: "mail");

            migrationBuilder.DropTable(
                name: "outgoing",
                schema: "mail");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "mail");

            migrationBuilder.DropTable(
                name: "templates",
                schema: "mail");
        }
    }
}
