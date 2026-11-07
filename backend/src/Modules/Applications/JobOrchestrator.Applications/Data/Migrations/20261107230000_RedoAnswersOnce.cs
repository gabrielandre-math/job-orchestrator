using JobOrchestrator.Applications.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <summary>
    /// Data only, once at the update. Nothing was sent for an application waiting for your approval or for your answer, and its form
    /// answers came from the rules before 1.2.4: a small model's "text" taken as an answer, questions in Portuguese read as Spanish and
    /// answered in English, the answer bank's "Não" on a long open question that only said "mudança". Their AI answers are dropped and
    /// the form is filled again, one a minute.
    /// </summary>
    [DbContext(typeof(ApplicationsDbContext))]
    [Migration("20261107230000_RedoAnswersOnce")]
    public partial class RedoAnswersOnce : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create temporary table redo on commit drop as
                  select a.id, a.state from applications.applications a
                  where a.archived_at is null and (a.state = 'AwaitingApproval'
                     or a.state = 'NeedsHuman'
                        and exists (select 1 from applications.human_tasks t where t.application_id = a.id and t.resolved_at is null and t.kind = 'Question')
                        and not exists (select 1 from applications.human_tasks t where t.application_id = a.id and t.resolved_at is null and t.kind <> 'Question'));
                delete from applications.form_answers where application_id in (select id from redo);
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, state, 'RetryScheduled', 'O app refaz as respostas deste formulário com as correções da 1.2.4: textos pelo seu currículo, na língua da pergunta, e o banco de respostas só em perguntas diretas.', 'Supervisor', now() from redo;
                update applications.human_tasks set resolved_at = now(), resolution = 'O app refaz as respostas'
                  where resolved_at is null and application_id in (select id from redo);
                insert into applications.work_items (id, application_id, step, run_after, attempts, max_attempts, created_at)
                  select gen_random_uuid(), id, 'Prepare', now() + (row_number() over (order by id) - 1) * interval '1 minute', 0, 4, now() from redo
                  where not exists (select 1 from applications.work_items w where w.application_id = redo.id and w.step = 'Prepare' and w.completed_at is null);
                update applications.applications set state = 'RetryScheduled', last_reason = 'O app vai refazer as respostas deste formulário.', updated_at = now()
                  where id in (select id from redo);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
