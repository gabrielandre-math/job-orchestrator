using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Mail.Data.Migrations
{
    /// <inheritdoc />
    public partial class AttachmentHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "attachment_sha256",
                schema: "mail",
                table: "outgoing",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attachment_sha256",
                schema: "mail",
                table: "outgoing");
        }
    }
}
