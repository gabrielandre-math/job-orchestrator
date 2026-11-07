using System.Text.RegularExpressions;

namespace JobOrchestrator.Jobs.Features;

/// <summary>How many jobs that point to LinkedIn came with each field, per way in (alert e-mail, capture by link).</summary>
internal sealed record LinkedInCoverage(string Source, int Jobs, int Title, int Company, int Location, int Description, int Salary, int ApplyEmail);

/// <summary>
/// LinkedIn does not allow robots, so its jobs only come from what you bring: alert e-mails and the pages you capture. This measures
/// what those paths really deliver, field by field, instead of calling LinkedIn "covered".
/// </summary>
internal sealed class LinkedInCoverageEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/api/jobs/linkedin-coverage", async (JobsDbContext db, CancellationToken ct) => Results.Ok(await QueryAsync(db, ct))).WithTags("Jobs");

    internal static Task<List<LinkedInCoverage>> QueryAsync(JobsDbContext db, CancellationToken ct) => db.Postings.AsNoTracking()
        .Where(p => p.Source.StartsWith("linkedin-session:") || Regex.IsMatch(p.ApplyUrl, @"^https://([A-Za-z0-9-]+\.)*linkedin\.com/jobs/view/"))
        .GroupBy(p => p.Source)
        .OrderByDescending(g => g.Count())
        .Select(g => new LinkedInCoverage(g.Key, g.Count(), g.Count(p => p.Title != ""), g.Count(p => p.Company != "" && p.Company != "Empresa a confirmar"), g.Count(p => p.Location != ""),
            g.Count(p => p.Description.Length >= 200), g.Count(p => p.SalaryText != null || p.SalaryMin != null), g.Count(p => p.ApplyEmail != null && p.ApplyEmail != "")))
        .ToListAsync(ct);
}
