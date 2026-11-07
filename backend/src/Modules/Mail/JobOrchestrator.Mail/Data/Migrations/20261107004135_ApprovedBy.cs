using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Mail.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApprovedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approved_by",
                schema: "mail",
                table: "outgoing",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approved_by",
                schema: "mail",
                table: "outgoing");
        }
    }
}
