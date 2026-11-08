using JobOrchestrator.Applications.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <summary>
    /// Data only, once at the update. The robot's send and its check are gone: their pending work closes, a send the robot left uncertain
    /// or a policy held goes to you (check your e-mail first: "Já enviei" if the confirmation came), and a form that stopped on a privacy
    /// or terms box is prepared again, since the prepare now ticks it in its own browser and you tick it on the site.
    /// </summary>
    [DbContext(typeof(ApplicationsDbContext))]
    [Migration("20261108090000_RobotSendGoneOnce")]
    public partial class RobotSendGoneOnce : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                update applications.work_items set completed_at = now() where completed_at is null and step in ('Submit', 'Reconcile');

                create temporary table stuck on commit drop as
                  select id, state, title, company from applications.applications
                  where archived_at is null and state in ('SubmissionUnknown', 'Submitting', 'BlockedByPolicy', 'AwaitingApproval', 'ReadyToSubmit', 'FormValidated');
                update applications.human_tasks set resolved_at = now(), resolution = 'Vai pelo site, com o copiloto'
                  where resolved_at is null and application_id in (select id from stuck);
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, state, 'ManualHandoff', case when state in ('SubmissionUnknown', 'Submitting')
                    then 'O robô clicou em enviar e a confirmação não apareceu. Confira o seu e-mail: se a confirmação chegou, marque "Já enviei"; se não, abra preenchida e envie.'
                    else 'Pronta para você enviar: abra preenchida, o copiloto completa o formulário e o clique em enviar é seu.' end, 'Supervisor', now() from stuck;
                insert into applications.human_tasks (id, application_id, kind, title, detail, created_at)
                  select gen_random_uuid(), id, 'Review', 'Candidatar-se pelo site da vaga', title || ', ' || company, now() from stuck;
                update applications.applications a set state = 'ManualHandoff', updated_at = now(),
                  last_reason = case when s.state in ('SubmissionUnknown', 'Submitting')
                    then 'O robô clicou em enviar e a confirmação não apareceu. Confira o seu e-mail: se a confirmação chegou, marque "Já enviei"; se não, abra preenchida e envie.'
                    else 'Pronta para você enviar. Abra preenchida: o copiloto completa o formulário e anexa o currículo, e o clique em enviar é seu.' end
                  from stuck s where a.id = s.id;

                create temporary table declaration on commit drop as
                  select id from applications.applications
                  where archived_at is null and state = 'NeedsHuman' and last_reason like 'O formulário pede uma declaração%';
                update applications.human_tasks set resolved_at = now(), resolution = 'O app prepara de novo'
                  where resolved_at is null and application_id in (select id from declaration);
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, 'NeedsHuman', 'RetryScheduled', 'A caixa de privacidade ou termos não para mais o preparo: ela é marcada só no navegador do preparo, e no site quem marca é você.', 'Supervisor', now() from declaration;
                insert into applications.work_items (id, application_id, step, run_after, attempts, max_attempts, created_at)
                  select gen_random_uuid(), id, 'Prepare', now() + (row_number() over (order by id) - 1) * interval '1 minute', 0, 4, now() from declaration
                  where not exists (select 1 from applications.work_items w where w.application_id = declaration.id and w.step = 'Prepare' and w.completed_at is null);
                update applications.applications set state = 'RetryScheduled', updated_at = now(),
                  last_reason = 'O app vai preparar de novo: a caixa de privacidade ou termos não para mais o formulário.'
                  where id in (select id from declaration);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
