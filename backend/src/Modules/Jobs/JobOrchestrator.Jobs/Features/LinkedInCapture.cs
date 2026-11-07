using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Jobs.Features;

internal sealed record LinkedInPage(string PageUrl, DateTimeOffset CapturedAt, IReadOnlyList<LinkedInPosting> Jobs);
internal sealed record LinkedInPosting(string Url, string Title, string? Company = null, string? Location = null,
    string? Description = null, string? Salary = null, string? PostedAt = null, string? ExternalApplyUrl = null);
internal sealed record CaptureLinkedInCommand(LinkedInPage Page) : ICommand<IReadOnlyList<LinkedInCaptured>>, IJobsCommand;
internal sealed record LinkedInCaptured(Guid JobId, string Url, bool Updated, string? ApplyEmail, string ContactStatus);

/// <summary>Normalizes only job identities. A page chosen by the user is the source, never a guessed contact or a login cookie.</summary>
internal static partial class LinkedInCapture
{
    public static bool Host(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && (uri.Host.Equals("linkedin.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".linkedin.com", StringComparison.OrdinalIgnoreCase));

    public static string? Canonical(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var u) || !Host(u)) return null;
        var match = JobPath().Match(u.AbsolutePath);
        return match.Success ? $"https://www.linkedin.com/jobs/view/{match.Groups[1].Value}/" : null;
    }

    public static PublishedContact Contact(string text) => PublishedContactExtractor.From(text);

    [GeneratedRegex(@"^/jobs/view/(?:[^/]+-)?([0-9]{1,24})/?$", RegexOptions.IgnoreCase)] private static partial Regex JobPath();

}

internal sealed class CaptureLinkedInValidator : AbstractValidator<CaptureLinkedInCommand>
{
    public CaptureLinkedInValidator()
    {
        RuleFor(x => x.Page).NotNull();
        When(x => x.Page is not null, () =>
        {
            RuleFor(x => x.Page.PageUrl).Must(s => Uri.TryCreate(s, UriKind.Absolute, out var u) && LinkedInCapture.Host(u)).WithMessage("Abra uma página HTTPS do LinkedIn.");
            RuleFor(x => x.Page.Jobs).NotNull().Must(j => j is { Count: > 0 and <= 20 }).WithMessage("Capture de uma a vinte vagas por vez.");
            When(x => x.Page.Jobs is not null, () => RuleForEach(x => x.Page.Jobs).ChildRules(j =>
            {
                j.RuleFor(x => x.Url).Must(s => LinkedInCapture.Canonical(s) is not null).WithMessage("Link de vaga do LinkedIn inválido.");
                j.RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
                j.RuleFor(x => x.Company).MaximumLength(200);
                j.RuleFor(x => x.Location).MaximumLength(200);
                j.RuleFor(x => x.Description).MaximumLength(20_000);
                j.RuleFor(x => x.Salary).MaximumLength(120);
                j.RuleFor(x => x.PostedAt).MaximumLength(120);
                j.RuleFor(x => x.ExternalApplyUrl).MaximumLength(1000).Must(s => string.IsNullOrEmpty(s) || Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme == "https" && u.UserInfo.Length == 0);
            }));
        });
    }
}

internal sealed class CaptureLinkedInHandler(JobsDbContext db, ICampaignsApi campaigns, TimeProvider time)
    : ICommandHandler<CaptureLinkedInCommand, IReadOnlyList<LinkedInCaptured>>
{
    public async Task<Result<IReadOnlyList<LinkedInCaptured>>> Handle(CaptureLinkedInCommand c, CancellationToken ct)
    {
        var campaign = (await campaigns.ListAsync(ct)).FirstOrDefault();
        if (campaign is null) return Error.NotFound("no-campaign", "Crie uma campanha antes de capturar vagas.");
        var now = time.GetUtcNow().UtcDateTime;
        var results = new List<LinkedInCaptured>();
        foreach (var input in c.Page.Jobs.DistinctBy(j => LinkedInCapture.Canonical(j.Url)))
        {
            var url = LinkedInCapture.Canonical(input.Url)!;
            var externalId = new Uri(url).AbsolutePath.Trim('/').Split('/').Last();
            var existing = await db.Postings.FirstOrDefaultAsync(p => p.CampaignId == campaign.Id && p.Source == "manual:linkedin.com" && (p.ExternalId == externalId || p.ApplyUrl == url), ct);
            var description = PostingIntegrity.Inspect(input.Description ?? "").Text;
            // Search cards do not erase a richer detail page captured earlier.
            if (existing is not null && description.Length < existing.Description.Length) description = existing.Description;
            var contact = LinkedInCapture.Contact(description);
            var company = string.IsNullOrWhiteSpace(input.Company) ? existing?.Company ?? "Empresa a confirmar" : input.Company.Trim();
            var location = string.IsNullOrWhiteSpace(input.Location) ? existing?.Location ?? "" : input.Location.Trim();
            var raw = JsonSerializer.Serialize(new
            {
                kind = "linkedin.capture", version = 1, url, pageUrl = c.Page.PageUrl, capturedAt = c.Page.CapturedAt, receivedAt = now,
                input.ExternalApplyUrl, input.PostedAt, contact,
                fields = new { title = "active_tab", company = input.Company is { Length: > 0 } ? "active_tab" : "unknown", location = input.Location is { Length: > 0 } ? "active_tab" : "unknown", description = input.Description is { Length: > 0 } ? "active_tab" : "not_in_card" }
            }, JsonOptions);
            var posting = new DiscoveredPosting("manual:linkedin.com", externalId, company, input.Title.Trim(), location, url, description, now, raw,
                Compensation: input.Salary, ApplyEmail: contact.Email, ListedAsOpen: false);
            var job = existing ?? JobPosting.Discover(campaign.Id, posting, now);
            if (existing is null) db.Postings.Add(job); else job.SeenAgain(posting, now);
            var shortlisted = job.Evaluate(Scoring.Evaluate(campaign, job.Title, job.Description, job.Location, job.Modality), campaign.MinFitScore, EligibilityPolicy.Manual);
            if (shortlisted)
                db.Publish(JobShortlistedV1.EventType, new JobShortlistedV1(job.Id, campaign.Id, job.CanonicalKey, job.Company, job.Title, job.Location, job.ApplyUrl, job.Score!.Value, job.ApplyChannel), now);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            db.Snapshots.Add(new JobSnapshot { Id = Guid.CreateVersion7(), JobId = job.Id, FetchedAt = now, PayloadHash = hash, Payload = RunRequestedConsumer.AsJson(raw) });
            results.Add(new(job.Id, url, existing is not null, contact.Email, contact.Status));
        }
        return Result<IReadOnlyList<LinkedInCaptured>>.Ok(results);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal sealed class LinkedInCaptureEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/inbox/linkedin", async (LinkedInPage page, ISender sender, CancellationToken ct) =>
            (await sender.Send(new CaptureLinkedInCommand(page), ct)).ToHttp(Results.Ok)).WithTags("Jobs");
        app.MapGet("/api/jobs/{id:guid}/source-evidence", async (Guid id, JobsDbContext db, CancellationToken ct) =>
        {
            var snapshot = await db.Snapshots.AsNoTracking().Where(s => s.JobId == id).OrderByDescending(s => s.FetchedAt).FirstOrDefaultAsync(ct);
            return snapshot is null ? Results.NotFound() : Results.Content(snapshot.Payload, "application/json");
        }).WithTags("Jobs");
    }
}
