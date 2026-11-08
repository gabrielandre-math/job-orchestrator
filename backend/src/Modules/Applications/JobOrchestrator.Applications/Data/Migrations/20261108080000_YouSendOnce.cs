using JobOrchestrator.Applications.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <summary>
    /// Data only, once at the update. The app no longer asks you to approve a send the robot cannot make: what waited for approval, was
    /// approved but not sent, or sat prepared goes to you, filled by the copilot, as "Para enviar".
    /// </summary>
    [DbContext(typeof(ApplicationsDbContext))]
    [Migration("20261108080000_YouSendOnce")]
    public partial class YouSendOnce : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create temporary table ready on commit drop as
                  select id, state, title, company from applications.applications
                  where archived_at is null and state in ('AwaitingApproval', 'ReadyToSubmit', 'FormValidated');
                update applications.human_tasks set resolved_at = now(), resolution = 'Vai pelo site, com o copiloto'
                  where resolved_at is null and application_id in (select id from ready);
                update applications.work_items set completed_at = now()
                  where completed_at is null and step = 'Submit' and application_id in (select id from ready);
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, state, 'ManualHandoff', 'Pronta para você enviar: abra preenchida, o copiloto completa o formulário e o clique em enviar é seu.', 'Supervisor', now() from ready;
                insert into applications.human_tasks (id, application_id, kind, title, detail, created_at)
                  select gen_random_uuid(), id, 'Review', 'Candidatar-se pelo site da vaga', title || ', ' || company, now() from ready;
                update applications.applications set state = 'ManualHandoff', updated_at = now(),
                  last_reason = 'Pronta para você enviar. Abra preenchida: o copiloto completa o formulário e anexa o currículo, e o clique em enviar é seu.'
                  where id in (select id from ready);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
