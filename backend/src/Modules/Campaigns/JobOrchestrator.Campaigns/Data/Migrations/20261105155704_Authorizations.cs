using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Campaigns.Data.Migrations
{
    /// <inheritdoc />
    public partial class Authorizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "authorizations",
                schema: "campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    max_per_day = table.Column<int>(type: "integer", nullable: false),
                    min_score = table.Column<int>(type: "integer", nullable: false),
                    adapters = table.Column<List<string>>(type: "text[]", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authorizations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_authorizations_campaign_id_granted_at",
                schema: "campaigns",
                table: "authorizations",
                columns: new[] { "campaign_id", "granted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authorizations",
                schema: "campaigns");
        }
    }
}
