using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Intelligence.Data.Migrations
{
    /// <inheritdoc />
    public partial class MessageData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "data",
                schema: "intelligence",
                table: "messages",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "data",
                schema: "intelligence",
                table: "messages");
        }
    }
}
