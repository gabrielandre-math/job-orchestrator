using System.Net;
using System.Text.RegularExpressions;
using JobOrchestrator.Automation.Contracts;

namespace JobOrchestrator.Mail.Features;

/// <summary>
/// Alerta de vaga por e-mail: a pessoa cria o alerta no LinkedIn, Indeed, Catho, InfoJobs, Glassdoor ou Vagas.com, e as vagas chegam na
/// caixa dela. Aqui cada link de vaga do alerta vira uma vaga com título, empresa e local. Nenhum desses sites é aberto por robô: o link
/// fica para o copiloto, com a pessoa.
/// </summary>
internal static partial class AlertParser
{
    public static bool IsAlert(string from, string subject) =>
        Sender().IsMatch(from) && (AlertSubject().IsMatch(subject) || from.Contains("alert", StringComparison.OrdinalIgnoreCase) || from.Contains("jobs", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<AlertJob> Parse(string from, string html, DateTime received)
    {
        var sender = Sender().Match(from) is { Success: true } s ? s.Value.ToLowerInvariant() : from;
        var anchors = Anchor().Matches(html).ToList();
        var jobs = new List<AlertJob>();
        for (var i = 0; i < anchors.Count; i++)
        {
            var href = WebUtility.HtmlDecode(anchors[i].Groups[1].Value);
            if (Normalize(href) is not { } url) continue;
            var text = Clean(anchors[i].Groups[2].Value);
            // O texto entre este link e o próximo costuma trazer "Empresa · Local" logo abaixo do título.
            var end = i + 1 < anchors.Count ? anchors[i + 1].Index : Math.Min(html.Length, anchors[i].Index + anchors[i].Length + 600);
            var after = Clean(html[(anchors[i].Index + anchors[i].Length)..end]);
            if (Generic().IsMatch(text) || text.Length < 4) { if (jobs.LastOrDefault() is { } prev && prev.Url == url) continue; text = ""; }
            if (text.Length == 0) continue;
            var parts = after.Split(['·', '|', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => p.Length > 1 && !Generic().IsMatch(p)).ToList();
            // "Empresa - Local" numa linha só (formato do Indeed e da Catho).
            if (parts.Count == 1 && parts[0].Contains(" - ", StringComparison.Ordinal)) parts = [.. parts[0].Split(" - ", 2, StringSplitOptions.TrimEntries)];
            jobs.Add(new AlertJob(sender, Cut(text, 160), Cut(parts.ElementAtOrDefault(0) ?? "", 120), Cut(parts.ElementAtOrDefault(1) ?? "", 120), url, Cut(after, 400), received));
        }
        return [.. jobs.GroupBy(j => j.Url).Select(g => g.First())];
    }

    /// <summary>Link de vaga sem rastreio: o mesmo anúncio em dois alertas vira uma vaga só.</summary>
    internal static string? Normalize(string href)
    {
        if (LinkedIn().Match(href) is { Success: true } li) return $"https://www.linkedin.com/jobs/view/{li.Groups[1].Value}/";
        if (Indeed().Match(href) is { Success: true } ind) return $"https://br.indeed.com/viewjob?jk={ind.Groups[1].Value}";
        if (!Uri.TryCreate(href, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http")) return null;
        var clean = $"{u.Scheme}://{u.Host}{u.AbsolutePath}".TrimEnd('/');
        return JobPath().IsMatch(clean) ? clean : null;
    }

    private static string Clean(string html) =>
        Spaces().Replace(WebUtility.HtmlDecode(Tags().Replace(html.Replace("<br>", "\n").Replace("<br/>", "\n").Replace("</p>", "\n").Replace("</td>", "\n").Replace("</div>", "\n"), " ")), " ").Replace(" \n", "\n").Trim();
    private static string Cut(string s, int n) => s.Length <= n ? s : s[..n];

    [GeneratedRegex(@"linkedin\.com|indeed\.com|glassdoor\.com|catho\.com\.br|infojobs\.com\.br|vagas\.com\.br|gupy\.io|trampos\.co|programathor|geekhunter|remotar|99jobs|bne\.com\.br|empregos\.com\.br|solides|jooble|ziprecruiter|getonbrd", RegexOptions.IgnoreCase)]
    private static partial Regex Sender();
    [GeneratedRegex(@"vaga|vagas|job|jobs|oportunidade|alerta|alert|recomendad|recommended|hiring|contratando", RegexOptions.IgnoreCase)] private static partial Regex AlertSubject();
    [GeneratedRegex(@"<a\b[^>]*href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex Anchor();
    [GeneratedRegex(@"linkedin\.com/(?:comm/)?jobs/view/(\d+)", RegexOptions.IgnoreCase)] private static partial Regex LinkedIn();
    [GeneratedRegex(@"indeed\.[a-z.]+/[^""\s]*[?&]jk=([0-9a-f]{8,})", RegexOptions.IgnoreCase)] private static partial Regex Indeed();
    [GeneratedRegex(@"/(?:vagas?|jobs?|job-listing|vaga-de-|oportunidades?|positions?|careers?/job|viewjob|jobListing|Detail)/?[\w%.,-]*\d|/vaga-de-[\w-]+|catho\.com\.br/vagas/[\w-]+|vagas\.com\.br/vagas/v\d+|gupy\.io/jobs/\d+", RegexOptions.IgnoreCase)]
    private static partial Regex JobPath();
    [GeneratedRegex(@"^(ver vaga|ver vagas|ver todas|ver mais|view job|view all|see all|apply|candidatar|candidate-se|saiba mais|learn more|unsubscribe|descadastrar|cancelar inscri|gerenciar|manage|\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Generic();
    [GeneratedRegex("<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex(@"[ \t\r\f\v]+")] private static partial Regex Spaces();
}

/// <summary>As vagas dos alertas guardadas pela leitura da caixa, para a fonte "alertas:email".</summary>
internal sealed class MailAlertFeed(MailDbContext db) : IJobAlertFeed
{
    public async Task<IReadOnlyList<AlertJob>> RecentAsync(DateTime since, CancellationToken ct) =>
        await db.AlertPostings.AsNoTracking().Where(a => a.ReceivedAt >= since).OrderByDescending(a => a.ReceivedAt).Take(500)
            .Select(a => new AlertJob(a.Sender, a.Title, a.Company, a.Location, a.Url, a.Snippet, a.ReceivedAt)).ToListAsync(ct);
}
