using JobOrchestrator.Applications.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <summary>
    /// Data only: applications that stopped with you over a form question (a required question, options to tick or a privacy box)
    /// go back to the form once, at the update. The AI now answers those questions from your profile's facts, and your
    /// authorization may accept the declarations.
    /// </summary>
    [DbContext(typeof(ApplicationsDbContext))]
    [Migration("20261107093000_RetryFormsWithAiAnswers")]
    public partial class RetryFormsWithAiAnswers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create temporary table stuck_form on commit drop as
                  select id from applications.applications
                  where state = 'NeedsHuman' and apply_channel = 'ats'
                    and (last_reason like 'Pergunta obrigatória sem resposta aprovada%' or last_reason like 'Perguntas obrigatórias sem resposta aprovada%'
                      or last_reason like 'O formulário pede para marcar opções%' or last_reason like 'O formulário pede uma declaração%');
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, 'NeedsHuman', 'RetryScheduled', 'A IA passou a responder as perguntas do formulário pelos fatos do perfil; o formulário é preenchido de novo.', 'Supervisor', now()
                  from stuck_form;
                update applications.human_tasks set resolved_at = now(), resolution = 'Formulário refeito com as respostas da IA'
                  where resolved_at is null and application_id in (select id from stuck_form);
                insert into applications.work_items (id, application_id, step, run_after, attempts, max_attempts, created_at)
                  select gen_random_uuid(), id, 'Prepare', now(), 0, 4, now() from stuck_form
                  where not exists (select 1 from applications.work_items w where w.application_id = stuck_form.id and w.step = 'Prepare' and w.completed_at is null);
                update applications.applications set state = 'RetryScheduled', last_reason = 'O formulário vai ser preenchido de novo, com as respostas da IA pelo seu perfil.', updated_at = now()
                  where id in (select id from stuck_form);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
