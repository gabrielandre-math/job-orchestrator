using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "applications");

            migrationBuilder.CreateTable(
                name: "applications",
                schema: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    apply_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resume_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resume_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    prepared_by = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    prepared_fields = table.Column<string>(type: "jsonb", nullable: false),
                    receipt_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    evidence_level = table.Column<int>(type: "integer", nullable: false),
                    last_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_decisions",
                schema: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    approved = table.Column<bool>(type: "boolean", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_decisions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "evidence",
                schema: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "human_tasks",
                schema: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolution = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_human_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox",
                schema: "applications",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox", x => x.message_id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "submission_intents",
                schema: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    adapter_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_intents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "work_items",
                schema: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    run_after = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                schema: "applications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    from = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    detail = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_events_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "applications",
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_applications_campaign_id_canonical_key",
                schema: "applications",
                table: "applications",
                columns: new[] { "campaign_id", "canonical_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_applications_state",
                schema: "applications",
                table: "applications",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_approval_decisions_application_id",
                schema: "applications",
                table: "approval_decisions",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_application_id_at",
                schema: "applications",
                table: "events",
                columns: new[] { "application_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_application_id",
                schema: "applications",
                table: "evidence",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_human_tasks_application_id",
                schema: "applications",
                table: "human_tasks",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_occurred_at",
                schema: "applications",
                table: "outbox",
                column: "occurred_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_submission_intents_application_id",
                schema: "applications",
                table: "submission_intents",
                column: "application_id",
                unique: true,
                filter: "state IN ('Open', 'SentUnverified')");

            migrationBuilder.CreateIndex(
                name: "ix_work_items_application_id_step",
                schema: "applications",
                table: "work_items",
                columns: new[] { "application_id", "step" },
                unique: true,
                filter: "completed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_work_items_run_after",
                schema: "applications",
                table: "work_items",
                column: "run_after",
                filter: "completed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_decisions",
                schema: "applications");

            migrationBuilder.DropTable(
                name: "events",
                schema: "applications");

            migrationBuilder.DropTable(
                name: "evidence",
                schema: "applications");

            migrationBuilder.DropTable(
                name: "human_tasks",
                schema: "applications");

            migrationBuilder.DropTable(
                name: "inbox",
                schema: "applications");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "applications");

            migrationBuilder.DropTable(
                name: "submission_intents",
                schema: "applications");

            migrationBuilder.DropTable(
                name: "work_items",
                schema: "applications");

            migrationBuilder.DropTable(
                name: "applications",
                schema: "applications");
        }
    }
}
