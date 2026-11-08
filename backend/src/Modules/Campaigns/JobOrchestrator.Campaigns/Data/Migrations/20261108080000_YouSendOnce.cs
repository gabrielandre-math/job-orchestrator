using JobOrchestrator.Campaigns.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Campaigns.Data.Migrations
{
    /// <summary>
    /// Data only, once at the update. The robot's send never got through a real site (reCAPTCHA, anti-robot checks, login), so the app
    /// prepares and you send with the copilot: campaigns that waited for approval or sent by themselves now prepare. The authorizations
    /// stay as they are.
    /// </summary>
    [DbContext(typeof(CampaignsDbContext))]
    [Migration("20261108080000_YouSendOnce")]
    public partial class YouSendOnce : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("update campaigns.campaigns set mode = 'PrepareOnly' where mode in ('ReviewRequired', 'AutoSubmit');");
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
