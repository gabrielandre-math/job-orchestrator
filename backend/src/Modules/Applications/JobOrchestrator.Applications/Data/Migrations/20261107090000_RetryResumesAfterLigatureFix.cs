using JobOrchestrator.Applications.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <summary>
    /// Data only: on macOS and Windows the PDF inspector read the "fi" ligature as one character, so every resume with "fi" failed
    /// ("Afirmações presentes no texto") and the application stopped with you, with no resume to retry from. Once, at the update,
    /// those applications go back to the resume step, with the reason in their history.
    /// </summary>
    [DbContext(typeof(ApplicationsDbContext))]
    [Migration("20261107090000_RetryResumesAfterLigatureFix")]
    public partial class RetryResumesAfterLigatureFix : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create temporary table stuck_resume on commit drop as
                  select id from applications.applications
                  where state = 'NeedsHuman' and last_reason like 'O currículo não ficou pronto: Afirmações presentes no texto%';
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, 'NeedsHuman', 'RetryScheduled', 'O inspetor do PDF lia a ligadura "fi" como um caractere só no macOS e no Windows; o currículo é refeito.', 'Supervisor', now()
                  from stuck_resume;
                update applications.human_tasks set resolved_at = now(), resolution = 'Currículo refeito depois da correção do inspetor'
                  where resolved_at is null and application_id in (select id from stuck_resume);
                insert into applications.work_items (id, application_id, step, run_after, attempts, max_attempts, created_at)
                  select gen_random_uuid(), id, 'Materials', now(), 0, 4, now() from stuck_resume;
                update applications.applications set state = 'RetryScheduled', last_reason = 'O currículo vai ser refeito: o inspetor do PDF foi corrigido.', updated_at = now()
                  where id in (select id from stuck_resume);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
