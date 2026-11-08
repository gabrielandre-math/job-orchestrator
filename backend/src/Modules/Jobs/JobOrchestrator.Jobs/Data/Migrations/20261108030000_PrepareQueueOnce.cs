using JobOrchestrator.Jobs.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOrchestrator.Jobs.Data.Migrations
{
    /// <summary>
    /// Data only, once at the update. Jobs that entered the queue while the campaign only searched never got an application, and
    /// leaving "só buscar" did not bring them back: the queue looked full and nothing was ever sent. Every open job in the queue goes to
    /// Applications again, which creates only the applications that are missing and skips campaigns that still only search.
    /// </summary>
    [DbContext(typeof(JobsDbContext))]
    [Migration("20261108030000_PrepareQueueOnce")]
    public partial class PrepareQueueOnce : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                insert into jobs.outbox (id, event_type, content, occurred_at, attempts)
                  select gen_random_uuid(), 'jobs.job-shortlisted.v1',
                    jsonb_build_object('jobId', p.id, 'campaignId', p.campaign_id, 'canonicalKey', p.canonical_key, 'company', p.company, 'title', p.title,
                      'location', coalesce(p.location, ''), 'applyUrl', p.apply_url, 'score', coalesce(p.score, 0), 'applyChannel', coalesce(p.apply_channel, 'ats')),
                    now(), 0
                  from jobs.postings p where p.open and p.status = 'Shortlisted';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
