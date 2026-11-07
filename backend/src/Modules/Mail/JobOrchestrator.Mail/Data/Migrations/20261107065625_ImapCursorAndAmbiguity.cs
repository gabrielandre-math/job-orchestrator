using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Mail.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImapCursorAndAmbiguity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ambiguous",
                schema: "mail",
                table: "incoming",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "uid_validity",
                schema: "mail",
                table: "accounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ambiguous",
                schema: "mail",
                table: "incoming");

            migrationBuilder.DropColumn(
                name: "uid_validity",
                schema: "mail",
                table: "accounts");
        }
    }
}
