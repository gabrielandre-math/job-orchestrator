using JobOrchestrator.Applications.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Applications.Data.Migrations
{
    /// <summary>
    /// Data only: links to sites without an adapter of their own went straight to you. The app now tries their open form, so once, at
    /// the update, the recent ones you have not marked as sent go back to the form; sites with an account or aggregators stay with you.
    /// </summary>
    [DbContext(typeof(ApplicationsDbContext))]
    [Migration("20261107110000_TrySiteFormsOnce")]
    public partial class TrySiteFormsOnce : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create temporary table site_link on commit drop as
                  select id from applications.applications
                  where state = 'ManualHandoff' and apply_channel = 'link' and resume_path is not null and created_at > now() - interval '30 days'
                    and apply_url !~* '^https?://([^/]*\.)?(linkedin\.com|indeed\.com|glassdoor\.com|glassdoor\.com\.br|gupy\.io|catho\.com\.br|infojobs\.com\.br|vagas\.com\.br|computrabajo\.com|pandape\.com\.br|solides\.com\.br|empregos\.com\.br|trabalhabrasil\.com\.br|getonbrd\.com|himalayas\.app|whatjobs\.com|wellfound\.com|angel\.co|arc\.dev|djinni\.co|geekhunter\.com|geekhunter\.com\.br|powertofly\.com|remoteok\.com|remoteok\.io|weworkremotely\.com|remotive\.com|remotive\.io|jooble\.org|adzuna\.com|adzuna\.com\.br|jobicy\.com|workingnomads\.com|themuse\.com|arbeitnow\.com|4dayweek\.io|github\.com|ycombinator\.com|upwork\.com|freelancer\.com|workana\.com|99freelas\.com\.br|programathor\.com\.br|trampos\.co|myworkdayjobs\.com|workday\.com|taleo\.net|icims\.com|oraclecloud\.com|successfactors\.com|successfactors\.eu|greenhouse\.io|lever\.co|ashbyhq\.com|workable\.com|smartrecruiters\.com|smartr\.me)(:\d+)?(/|$)';
                insert into applications.events (application_id, "from", "to", reason, actor, at)
                  select id, 'ManualHandoff', 'RetryScheduled', 'O app passou a tentar o formulário aberto do site; a candidatura volta para o preenchimento.', 'Supervisor', now()
                  from site_link;
                update applications.human_tasks set resolved_at = now(), resolution = 'O app tenta o formulário aberto do site'
                  where resolved_at is null and application_id in (select id from site_link);
                -- One every two minutes: the browser opens one site at a time and a small machine stays usable.
                insert into applications.work_items (id, application_id, step, run_after, attempts, max_attempts, created_at)
                  select gen_random_uuid(), id, 'Prepare', now() + (row_number() over (order by id) - 1) * interval '2 minutes', 0, 4, now() from site_link
                  where not exists (select 1 from applications.work_items w where w.application_id = site_link.id and w.step = 'Prepare' and w.completed_at is null);
                update applications.applications set state = 'RetryScheduled', last_reason = 'O app vai tentar o formulário aberto do site.', updated_at = now()
                  where id in (select id from site_link);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
