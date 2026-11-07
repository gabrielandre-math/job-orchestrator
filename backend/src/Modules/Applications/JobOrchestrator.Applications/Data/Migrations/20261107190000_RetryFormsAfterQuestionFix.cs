using JobOrchestrator.Applications.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <summary>
    /// Data only: in 1.1.0 the questions a form asked were lost on the way back, so the AI never answered them and the applications
    /// stopped with you again. Once, at the update, the ones stopped by a form question go back to the form, platforms and sites alike.
    /// </summary>
    [DbContext(typeof(ApplicationsDbContext))]
    [Migration("20261107190000_RetryFormsAfterQuestionFix")]
    public partial class RetryFormsAfterQuestionFix : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create temporary table stuck_question on commit drop as
                  select id from applications.applications
                  where state = 'NeedsHuman' and apply_channel in ('ats', 'link')
                    and (last_reason like 'Pergunta obrigatória sem resposta aprovada%' or last_reason like 'Perguntas obrigatórias sem resposta aprovada%'
                      or last_reason like 'O formulário pede para marcar opções%');
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, 'NeedsHuman', 'RetryScheduled', 'As perguntas do formulário agora chegam à IA; o formulário é preenchido de novo.', 'Supervisor', now()
                  from stuck_question;
                update applications.human_tasks set resolved_at = now(), resolution = 'Formulário refeito com as respostas da IA'
                  where resolved_at is null and application_id in (select id from stuck_question);
                insert into applications.work_items (id, application_id, step, run_after, attempts, max_attempts, created_at)
                  select gen_random_uuid(), id, 'Prepare', now() + (row_number() over (order by id) - 1) * interval '1 minute', 0, 4, now() from stuck_question
                  where not exists (select 1 from applications.work_items w where w.application_id = stuck_question.id and w.step = 'Prepare' and w.completed_at is null);
                update applications.applications set state = 'RetryScheduled', last_reason = 'O formulário vai ser preenchido de novo, com as respostas da IA pelo seu perfil.', updated_at = now()
                  where id in (select id from stuck_question);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
