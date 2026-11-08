using JobOrchestrator.Applications.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <summary>
    /// Data only, once at the update. Greenhouse job boards check at the send that a person is there (invisible reCAPTCHA, then a code
    /// mailed to the applicant): every send the robot clicked there was held, not accepted. Those, and the ones waiting for approval or
    /// to be sent there, go to you, filled by the copilot.
    /// </summary>
    [DbContext(typeof(ApplicationsDbContext))]
    [Migration("20261108070000_PersonCheckSitesOnce")]
    public partial class PersonCheckSitesOnce : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create temporary table held on commit drop as
                  select id, state, title, company from applications.applications
                  where archived_at is null and substring(apply_url from '://([^/:]+)') in ('job-boards.greenhouse.io', 'boards.greenhouse.io')
                    and (state in ('SubmissionUnknown', 'AwaitingApproval', 'ReadyToSubmit')
                      or (state = 'NeedsHuman' and last_reason like 'Este ATS não tem consulta de candidatura%')
                      or (state = 'BlockedByPolicy' and last_reason like 'Já existe um envio em andamento%'));
                update applications.submission_intents set state = 'Discarded', closed_at = now(),
                  note = 'O Greenhouse segurou o envio do robô pedindo um código no seu e-mail: a candidatura não foi aceita.'
                  where state in ('Open', 'SentUnverified') and application_id in (select id from held);
                update applications.human_tasks set resolved_at = now(), resolution = 'Vai pelo site, com o copiloto'
                  where resolved_at is null and application_id in (select id from held);
                update applications.work_items set completed_at = now()
                  where completed_at is null and step in ('Submit', 'Reconcile') and application_id in (select id from held);
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, state, 'ManualHandoff', 'O Greenhouse confirma no envio que é uma pessoa e segura o envio do robô com um código no seu e-mail: nada foi aceito. Abra preenchida e envie você.', 'Supervisor', now() from held;
                insert into applications.human_tasks (id, application_id, kind, title, detail, created_at)
                  select gen_random_uuid(), id, 'Review', 'Candidatar-se pelo site da vaga', title || ', ' || company, now() from held;
                update applications.applications set state = 'ManualHandoff', updated_at = now(),
                  last_reason = 'Pronta para você enviar. O site confirma no envio que é uma pessoa (reCAPTCHA): abra preenchida, o clique em enviar é seu.'
                  where id in (select id from held);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
