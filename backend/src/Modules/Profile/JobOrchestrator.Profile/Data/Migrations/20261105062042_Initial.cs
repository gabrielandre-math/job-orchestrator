using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Profile.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "profile");

            migrationBuilder.CreateTable(
                name: "inbox",
                schema: "profile",
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
                schema: "profile",
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
                name: "profiles",
                schema: "profile",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    synthetic = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    full_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    location = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    linked_in = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    git_hub = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    website = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    confirmations = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "profile",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stored_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    pages = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    extracted_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    imported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    extracted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_documents_profiles_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "profile",
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "facts",
                schema: "profile",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    statement = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organization = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    role = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    start = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    end = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    technologies = table.Column<List<string>>(type: "text[]", nullable: false),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    quote = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    page = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facts", x => x.id);
                    table.ForeignKey(
                        name: "fk_facts_profiles_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "profile",
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documents_profile_id_sha256",
                schema: "profile",
                table: "documents",
                columns: new[] { "profile_id", "sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_facts_profile_id_kind_position",
                schema: "profile",
                table: "facts",
                columns: new[] { "profile_id", "kind", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_occurred_at",
                schema: "profile",
                table: "outbox",
                column: "occurred_at",
                filter: "published_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documents",
                schema: "profile");

            migrationBuilder.DropTable(
                name: "facts",
                schema: "profile");

            migrationBuilder.DropTable(
                name: "inbox",
                schema: "profile");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "profile");

            migrationBuilder.DropTable(
                name: "profiles",
                schema: "profile");
        }
    }
}
