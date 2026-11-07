using JobOrchestrator.Applications.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <summary>
    /// Data only, once at the update. Back to the form: a send the site refused over a privacy box the app could not tick (it now asks
    /// first), site links stopped by a question before questions reached the AI, declarations (now asked in the app), expired WhatJobs
    /// links (now closed by the app) and applications that found their adapter paused. To "com você": what stopped on a CAPTCHA, a login, an anti-robot check or a
    /// page with no form, so "precisa de você" keeps only what needs your answer.
    /// </summary>
    [DbContext(typeof(ApplicationsDbContext))]
    [Migration("20261107220000_ClearFrictionOnce")]
    public partial class ClearFrictionOnce : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create temporary table back_to_form on commit drop as
                  select id, state from applications.applications
                  where state = 'Failed' and apply_channel in ('ats', 'link') and last_reason = 'O ATS recusou o formulário e mostrou erro de validação.'
                     or state = 'ManualHandoff' and apply_channel = 'link' and (last_reason like '%sem resposta aprovada%' or last_reason like '%pede uma declaração%' or apply_url ilike '%whatjobs.com/%')
                     or state = 'NeedsHuman' and apply_channel = 'ats' and last_reason = 'Nenhum adapter testado atende este domínio e esta etapa.'
                     -- A declaration now comes back as a question you can accept in the app.
                     or state = 'NeedsHuman' and last_reason like 'O formulário pede uma declaração%';
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, state, 'RetryScheduled', 'O app voltou a tentar este formulário depois das correções da 1.2.3.', 'Supervisor', now() from back_to_form;
                update applications.human_tasks set resolved_at = now(), resolution = 'O app tenta de novo'
                  where resolved_at is null and application_id in (select id from back_to_form);
                insert into applications.work_items (id, application_id, step, run_after, attempts, max_attempts, created_at)
                  select gen_random_uuid(), id, 'Prepare', now() + (row_number() over (order by id) - 1) * interval '1 minute', 0, 4, now() from back_to_form
                  where not exists (select 1 from applications.work_items w where w.application_id = back_to_form.id and w.step = 'Prepare' and w.completed_at is null);
                update applications.applications set state = 'RetryScheduled', last_reason = 'O app vai tentar este formulário de novo.', updated_at = now()
                  where id in (select id from back_to_form);

                create temporary table to_you on commit drop as
                  select id from applications.applications
                  where state = 'NeedsHuman' and (last_reason like '%CAPTCHA%' or last_reason like '%anti-robô%' or last_reason like '%pede login%'
                    or last_reason like 'Não há formulário%' or last_reason like '%não ficou marcad%' or last_reason like '%não guardou o valor%')
                    and not exists (select 1 from applications.human_tasks t where t.application_id = applications.id and t.resolved_at is null and t.kind = 'Reconcile');
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, 'NeedsHuman', 'ManualHandoff', 'Bloqueio do próprio site, que nenhuma resposta sua resolve: a candidatura vai pelo site original.', 'Supervisor', now() from to_you;
                update applications.human_tasks set resolved_at = now(), resolution = 'Vai pelo site original'
                  where resolved_at is null and application_id in (select id from to_you);
                insert into applications.human_tasks (id, application_id, kind, title, detail, created_at)
                  select gen_random_uuid(), a.id, 'Review', 'Candidatar-se pelo site da vaga', left(a.title || ', ' || a.company, 500), now()
                  from applications.applications a where a.id in (select id from to_you);
                update applications.applications set state = 'ManualHandoff', last_reason = left('A candidatura vai pelo site original. ' || last_reason, 500), updated_at = now()
                  where id in (select id from to_you);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
