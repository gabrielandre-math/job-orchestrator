using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Campaigns.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuthorizationDeclarations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "accept_declarations",
                schema: "campaigns",
                table: "authorizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accept_declarations",
                schema: "campaigns",
                table: "authorizations");
        }
    }
}
