using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Applications.Features.Workflow;

internal sealed record ProfileExperience(string Company, string Title, string Start, string? End, string[] Technologies, string[] Highlights);
internal sealed record ProfileEducation(string Institution, string Degree, string? End);

/// <summary>Perfil do candidato em arquivo. Na fase 3 vira o módulo Profile, com fatos verificados e evidências.</summary>
internal sealed record CandidateProfile(
    string FirstName, string LastName, string Email, string Phone, string LinkedIn, string Headline, string Location, string Summary,
    ProfileExperience[] Experiences, ProfileEducation[] Education, string[] Languages, Dictionary<string, string> Answers, bool Synthetic);

/// <summary>Lê o perfil e monta o pacote do formulário. Anos com cada tecnologia saem das datas das experiências.</summary>
internal sealed class ProfileSource(IConfiguration config, TimeProvider time)
{
    public CandidateProfile Load()
    {
        var path = config["Profile:Path"] is { Length: > 0 } p ? p : Path.Combine(AppPaths.Data, "profile.json");
        if (!File.Exists(path)) throw new FileNotFoundException("Perfil não encontrado. Importe o seu currículo.", path);
        return JsonSerializer.Deserialize<CandidateProfile>(File.ReadAllText(path), JsonSerializerOptions.Web)!;
    }

    public MaterialBundle Bundle(CandidateProfile p, string resumePath)
    {
        var answers = new Dictionary<string, string>(p.Answers, StringComparer.Ordinal)
        {
            ["years_csharp"] = YearsWith(p, "C#").ToString()
        };
        return new MaterialBundle(p.FirstName, p.LastName, p.Email, p.Phone, p.LinkedIn, resumePath, answers);
    }

    /// <summary>Soma os meses das experiências que citam a tecnologia, sem contar sobreposição.</summary>
    internal int YearsWith(CandidateProfile p, string technology)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var months = p.Experiences
            .Where(e => e.Technologies.Contains(technology, StringComparer.OrdinalIgnoreCase))
            .SelectMany(e => Months(e.Start, e.End, today)).Distinct().Count();
        return months / 12;
    }

    private static IEnumerable<int> Months(string start, string? end, DateOnly today)
    {
        var from = DateOnly.ParseExact(start + "-01", "yyyy-MM-dd");
        var to = end is { Length: > 0 } ? DateOnly.ParseExact(end + "-01", "yyyy-MM-dd") : today;
        for (var d = from; d <= to; d = d.AddMonths(1)) yield return d.Year * 12 + d.Month;
    }
}

/// <summary>
/// Currículo ATS-friendly em HTML para virar PDF: uma coluna, texto pesquisável, fontes do sistema, sem rede.
/// Só reordena e seleciona fatos do perfil; não escreve nada que não esteja lá.
/// </summary>
internal static partial class ResumeTemplate
{
    public static string Render(CandidateProfile p, JobApplication app)
    {
        var en = English().IsMatch(app.Title);
        string T(string pt, string english) => en ? english : pt;
        string E(string s) => WebUtility.HtmlEncode(s);
        var wanted = Words().Matches(app.Title.ToLowerInvariant()).Select(m => m.Value).ToHashSet();
        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html><html lang="{{(en ? "en" : "pt-BR")}}"><head><meta charset="utf-8"><title>{{E(p.FirstName)}} {{E(p.LastName)}}</title>
            <style>
              body { font-family: "Inter", "Liberation Sans", "DejaVu Sans", sans-serif; color: #16181d; font-size: 10.5pt; line-height: 1.42; margin: 0; }
              h1 { font-size: 20pt; margin: 0 0 2pt; letter-spacing: -0.01em; }
              .headline { font-size: 11pt; color: #3a3f4b; margin: 0 0 6pt; }
              .contact { font-size: 9.5pt; color: #3a3f4b; margin-bottom: 12pt; }
              h2 { font-size: 10pt; text-transform: uppercase; letter-spacing: 0.08em; color: #3a3f4b; border-bottom: 1px solid #d5d8de; padding-bottom: 2pt; margin: 14pt 0 6pt; }
              .role { display: flex; justify-content: space-between; font-weight: 600; margin-top: 8pt; }
              .dates { font-weight: 400; color: #3a3f4b; white-space: nowrap; }
              .tech { color: #3a3f4b; font-size: 9.5pt; margin: 1pt 0 3pt; }
              ul { margin: 2pt 0 0 14pt; padding: 0; } li { margin: 1.5pt 0; }
            </style></head><body>
            <h1>{{E(p.FirstName)}} {{E(p.LastName)}}</h1>
            <p class="headline">{{E(p.Headline)}}</p>
            <p class="contact">{{E(p.Location)}} | {{E(p.Email)}} | {{E(p.Phone)}} | {{E(p.LinkedIn)}}</p>
            <h2>{{T("Resumo", "Summary")}}</h2><p>{{E(p.Summary)}}</p>
            <h2>{{T("Experiência", "Experience")}}</h2>
            """);
        foreach (var x in p.Experiences.OrderByDescending(x => x.End ?? "9999").ThenByDescending(x => x.Start))
        {
            var highlights = x.Highlights.OrderByDescending(h => wanted.Count(w => h.Contains(w, StringComparison.OrdinalIgnoreCase)));
            sb.Append($"""<div class="role"><span>{E(x.Title)}, {E(x.Company)}</span><span class="dates">{E(x.Start)} – {E(x.End ?? T("atual", "present"))}</span></div>""");
            sb.Append($"""<div class="tech">{E(string.Join(", ", x.Technologies))}</div><ul>""");
            foreach (var h in highlights) sb.Append($"<li>{E(h)}</li>");
            sb.Append("</ul>");
        }
        sb.Append($"<h2>{T("Formação", "Education")}</h2><ul>");
        foreach (var ed in p.Education) sb.Append($"<li>{E(ed.Degree)}, {E(ed.Institution)}{(ed.End is null ? "" : $" ({E(ed.End)})")}</li>");
        sb.Append($"</ul><h2>{T("Idiomas", "Languages")}</h2><p>{E(string.Join(", ", p.Languages))}</p>");
        var skills = p.Experiences.SelectMany(x => x.Technologies).Distinct(StringComparer.OrdinalIgnoreCase);
        sb.Append($"<h2>{T("Tecnologias", "Skills")}</h2><p>{E(string.Join(", ", skills))}</p></body></html>");
        return sb.ToString();
    }

    [GeneratedRegex(@"engineer|developer|software|backend|full ?stack", RegexOptions.IgnoreCase)] private static partial Regex English();
    [GeneratedRegex(@"[a-z#.+]{2,}")] private static partial Regex Words();
}
