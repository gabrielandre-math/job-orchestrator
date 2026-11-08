using JobOrchestrator.Applications.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <summary>
    /// Data only, once at the update. Approved applications that a site refused when the robot clicked send (Workable answers "Something
    /// went wrong" to a robot) go by the site, where the copilot opens them filled; the ones that met an adapter paused by unconfirmed sends
    /// (nothing was clicked) are prepared again.
    /// </summary>
    [DbContext(typeof(ApplicationsDbContext))]
    [Migration("20261108050000_SiteAfterRefusalOnce")]
    public partial class SiteAfterRefusalOnce : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create temporary table refused on commit drop as
                  select id, title, company from applications.applications
                  where archived_at is null and state = 'NeedsHuman' and last_reason like 'O site recusou o formulário:%';
                update applications.human_tasks set resolved_at = now(), resolution = 'Vai pelo site, com o copiloto'
                  where resolved_at is null and application_id in (select id from refused);
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, 'NeedsHuman', 'ManualHandoff', 'O site recusou o envio do robô: abra preenchida e envie você, o copiloto completa o formulário.', 'Supervisor', now() from refused;
                insert into applications.human_tasks (id, application_id, kind, title, detail, created_at)
                  select gen_random_uuid(), id, 'Review', 'Candidatar-se pelo site da vaga', title || ', ' || company, now() from refused;
                update applications.applications set state = 'ManualHandoff', last_reason = 'O site recusou o envio do robô: abra preenchida e envie você.', updated_at = now()
                  where id in (select id from refused);

                create temporary table paused on commit drop as
                  select id from applications.applications
                  where archived_at is null and state = 'NeedsHuman' and last_reason like 'Nada foi enviado: Nenhum adapter testado%';
                update applications.human_tasks set resolved_at = now(), resolution = 'O app prepara de novo'
                  where resolved_at is null and application_id in (select id from paused);
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, 'NeedsHuman', 'RetryScheduled', 'O adapter estava em pausa quando o envio aprovado saiu: nada foi clicado, e o app prepara de novo.', 'Supervisor', now() from paused;
                insert into applications.work_items (id, application_id, step, run_after, attempts, max_attempts, created_at)
                  select gen_random_uuid(), id, 'Prepare', now() + (row_number() over (order by id) - 1) * interval '1 minute', 0, 4, now() from paused
                  where not exists (select 1 from applications.work_items w where w.application_id = paused.id and w.step = 'Prepare' and w.completed_at is null);
                update applications.applications set state = 'RetryScheduled', last_reason = 'O app vai preparar de novo: o envio aprovado encontrou o adapter em pausa.', updated_at = now()
                  where id in (select id from paused);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
