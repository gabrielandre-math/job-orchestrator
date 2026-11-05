using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Jobs.Features;

/// <summary>O que aconteceu com o link salvo.</summary>
internal sealed record CaptureResult(string Kind, Guid? JobId, string? Source, string Message);

/// <summary>
/// Comando: "Salvar vaga". Link de ATS conhecido vira empresa vigiada (a rodada traz os dados oficiais);
/// link de site com login (LinkedIn, Gupy) vira vaga manual com o texto que você selecionou, sem robô navegando lá.
/// </summary>
internal sealed record CaptureJobCommand(string Url, string? Title, string? Text) : ICommand<CaptureResult>, IJobsCommand;

internal sealed class CaptureJobValidator : AbstractValidator<CaptureJobCommand>
{
    public CaptureJobValidator()
    {
        RuleFor(x => x.Url).Must(u => Uri.TryCreate(u, UriKind.Absolute, out var x) && (x.Scheme == "https" || x.Scheme == "http")).WithMessage("Link inválido.");
        RuleFor(x => x.Text).MaximumLength(20_000).WithMessage("Texto grande demais.");
    }
}

internal sealed partial class CaptureJobHandler(JobsDbContext db, ICampaignsApi campaigns, IAutomationApi automation, TimeProvider time) : ICommandHandler<CaptureJobCommand, CaptureResult>
{
    public async Task<Result<CaptureResult>> Handle(CaptureJobCommand c, CancellationToken ct)
    {
        var campaign = (await campaigns.ListAsync(ct)).FirstOrDefault();
        if (campaign is null) return Error.NotFound("no-campaign", "Crie uma campanha antes de salvar vagas.");
        var now = time.GetUtcNow().UtcDateTime;
        var url = new Uri(c.Url);

        if (AtsSource(url) is { } source)
        {
            db.Publish(SourceDiscoveredV1.EventType, new SourceDiscoveredV1(campaign.Id, source, now), now);
            var known = campaign.Sources.Contains(source, StringComparer.OrdinalIgnoreCase);
            return new CaptureResult("watch", null, source, known
                ? "A empresa já é vigiada. A vaga entra na próxima rodada com os dados oficiais."
                : "A empresa entrou na vigilância da campanha. A rodada começa agora e traz a vaga com os dados oficiais.");
        }

        // Página de carreira de empresa (sem ATS conhecido e fora dos sites com login): procura os boards citados nela.
        if (Normalizer.Channel(c.Url, null) == "link" && string.IsNullOrWhiteSpace(c.Text))
        {
            var (boards, detail) = await automation.FindBoardsAsync(c.Url, ct);
            foreach (var board in boards) db.Publish(SourceDiscoveredV1.EventType, new SourceDiscoveredV1(campaign.Id, board, now), now);
            if (boards.Count > 0)
                return new CaptureResult("watch", null, string.Join(", ", boards), $"Achei {string.Join(", ", boards)} na página. A campanha passa a vigiar e roda agora.");
            if (detail.StartsWith("O robots.txt", StringComparison.Ordinal)) return new CaptureResult("blocked", null, null, detail);
        }

        var (company, role) = FromTitle(c.Title ?? "");
        var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(c.Url)))[..20];
        var origin = $"manual:{url.Host.Replace("www.", "")}";
        if (await db.Postings.AnyAsync(p => p.CampaignId == campaign.Id && p.Source == origin && p.ExternalId == id, ct))
            return new CaptureResult("known", null, origin, "Esta vaga já estava salva.");
        var raw = JsonSerializer.Serialize(new { c.Url, c.Title }, JsonSerializerOptions.Web);
        var posting = JobPosting.Discover(campaign.Id, new DiscoveredPosting(origin, id, company, role, "", c.Url, c.Text ?? "", now, raw), now);
        db.Postings.Add(posting);
        var shortlisted = posting.Evaluate(Scoring.Evaluate(campaign, posting.Title, posting.Description, posting.Location, posting.Modality), campaign.MinFitScore);
        if (shortlisted)
            db.Publish(JobShortlistedV1.EventType, new JobShortlistedV1(posting.Id, campaign.Id, posting.CanonicalKey, posting.Company, posting.Title, posting.Location, posting.ApplyUrl, posting.Score!.Value, posting.ApplyChannel), now);
        return new CaptureResult("saved", posting.Id, origin, string.IsNullOrWhiteSpace(c.Text)
            ? "Vaga salva só com o link. Selecione o texto da descrição antes de salvar para ela ganhar nota."
            : shortlisted ? $"Vaga salva com nota {posting.Score}. Entrou na fila; a candidatura vai pelo site original, com o kit pronto." : $"Vaga salva com nota {posting.Score ?? 0}. {posting.Reason}");
    }

    /// <summary>Board do ATS a partir do link da vaga ("boards.greenhouse.io/acme/jobs/1" vira "greenhouse:acme").</summary>
    internal static string? AtsSource(Uri url)
    {
        var parts = url.AbsolutePath.Trim('/').Split('/');
        var host = url.Host.ToLowerInvariant();
        if (parts.Length == 0 || parts[0].Length == 0) return null;
        if (host.EndsWith("greenhouse.io")) return parts[0] == "embed" ? null : $"greenhouse:{parts[0]}";
        if (host == "jobs.lever.co") return $"lever:{parts[0]}";
        if (host == "jobs.ashbyhq.com") return $"ashby:{parts[0]}";
        if (host == "jobs.smartrecruiters.com") return $"smartrecruiters:{parts[0]}";
        return null;
    }

    /// <summary>"Empresa hiring Cargo in Lugar | LinkedIn", "Cargo at Empresa" ou "Cargo - Empresa".</summary>
    internal static (string Company, string Role) FromTitle(string title)
    {
        var t = Suffix().Replace(Counter().Replace(title, ""), "").Trim();
        if (Hiring().Match(t) is { Success: true } h) return (h.Groups[1].Value.Trim(), h.Groups[2].Value.Trim());
        if (At().Match(t) is { Success: true } a) return (a.Groups[2].Value.Trim(), a.Groups[1].Value.Trim());
        if (Dash().Match(t) is { Success: true } d) return (d.Groups[2].Value.Trim(), d.Groups[1].Value.Trim());
        return ("Empresa a confirmar", t.Length > 0 ? t : "Vaga salva");
    }

    [GeneratedRegex(@"^\(\d+\)\s*")] private static partial Regex Counter();
    [GeneratedRegex(@"\s*[|·]\s*(LinkedIn|Gupy|Indeed|Glassdoor).*$", RegexOptions.IgnoreCase)] private static partial Regex Suffix();
    [GeneratedRegex(@"^(.+?)\s+(?:hiring|contrata)\s+(.+?)(?:\s+in\s+.+)?$", RegexOptions.IgnoreCase)] private static partial Regex Hiring();
    [GeneratedRegex(@"^(.+?)\s+(?:at|na|no|em)\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex At();
    [GeneratedRegex(@"^(.+?)\s+[-–]\s+(.+)$")] private static partial Regex Dash();
}

internal sealed class CaptureEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/api/inbox", async (CaptureJobCommand body, ISender sender, CancellationToken ct) => (await sender.Send(body, ct)).ToHttp(Results.Ok)).WithTags("Jobs");
}
