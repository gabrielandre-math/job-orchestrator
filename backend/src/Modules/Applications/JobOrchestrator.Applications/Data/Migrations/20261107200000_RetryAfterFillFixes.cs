using JobOrchestrator.Applications.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <summary>
    /// Data only: up to 1.1.1 a Workable option whose text sits in its own label was never ticked, and a refused value was tried four
    /// times; the application ended in the dead letter and the Workable adapter paused itself, so others found no adapter. Once, at
    /// the update, those go back to the form.
    /// </summary>
    [DbContext(typeof(ApplicationsDbContext))]
    [Migration("20261107200000_RetryAfterFillFixes")]
    public partial class RetryAfterFillFixes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create temporary table stuck_fill on commit drop as
                  select id, state from applications.applications
                  where apply_channel in ('ats', 'link') and (
                    state = 'DeadLetter' and (last_reason like '%não ficou marcada%' or last_reason like '%não guardou o valor%')
                    or state = 'NeedsHuman' and apply_channel = 'ats' and last_reason = 'Nenhum adapter testado atende este domínio e esta etapa.');
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, state, 'RetryScheduled', 'O preenchimento do Workable foi corrigido; o formulário é preenchido de novo.', 'Supervisor', now()
                  from stuck_fill;
                update applications.human_tasks set resolved_at = now(), resolution = 'Formulário refeito depois da correção'
                  where resolved_at is null and application_id in (select id from stuck_fill);
                insert into applications.work_items (id, application_id, step, run_after, attempts, max_attempts, created_at)
                  select gen_random_uuid(), id, 'Prepare', now() + (row_number() over (order by id) - 1) * interval '1 minute', 0, 4, now() from stuck_fill
                  where not exists (select 1 from applications.work_items w where w.application_id = stuck_fill.id and w.step = 'Prepare' and w.completed_at is null);
                update applications.applications set state = 'RetryScheduled', last_reason = 'O formulário vai ser preenchido de novo, com a correção do preenchimento.', updated_at = now()
                  where id in (select id from stuck_fill);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
