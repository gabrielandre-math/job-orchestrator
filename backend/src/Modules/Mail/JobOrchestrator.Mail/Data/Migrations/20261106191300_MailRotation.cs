using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Mail.Data.Migrations
{
    /// <inheritdoc />
    public partial class MailRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                schema: "mail",
                table: "outgoing",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pause_reason",
                schema: "mail",
                table: "accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "paused_until",
                schema: "mail",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "sends",
                schema: "mail",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // E-mails sent before rotation existed went out through the only (oldest) account.
            migrationBuilder.Sql("update mail.outgoing set account_id = (select id from mail.accounts order by created_at limit 1) where account_id is null and sent_at is not null;");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_account_id_sent_at",
                schema: "mail",
                table: "outgoing",
                columns: new[] { "account_id", "sent_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outgoing_account_id_sent_at",
                schema: "mail",
                table: "outgoing");

            migrationBuilder.DropColumn(
                name: "account_id",
                schema: "mail",
                table: "outgoing");

            migrationBuilder.DropColumn(
                name: "pause_reason",
                schema: "mail",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "paused_until",
                schema: "mail",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "sends",
                schema: "mail",
                table: "accounts");
        }
    }
}
