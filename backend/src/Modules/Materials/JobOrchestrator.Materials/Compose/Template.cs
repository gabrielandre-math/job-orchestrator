using System.Net;
using System.Text;

namespace JobOrchestrator.Materials.Compose;

/// <summary>
/// Currículo ATS-friendly: uma coluna, texto pesquisável, fontes do sistema, sem rede, sem tabela nem imagem.
/// Os dados pessoais entram como placeholders ({{NOME}}, {{EMAIL}}…) e só são trocados em Fill, na hora do PDF.
/// </summary>
internal static class Template
{
    /// <summary>
    /// Os temas mudam só tipografia, cor dos títulos e espaçamento: a estrutura continua uma coluna de texto pesquisável, sem tabela nem
    /// imagem, e passa no Inspetor ATS do mesmo jeito.
    /// </summary>
    public static readonly (string Key, string Name, string Note)[] Themes =
    [
        ("classico", "Clássico", "sans-serif sóbria, títulos em caixa alta com linha"),
        ("moderno", "Moderno", "nome maior e títulos com um traço de cor"),
        ("compacto", "Compacto", "margens e letra menores, para caber em uma página"),
        ("serifado", "Serifado", "letra com serifa, tom mais formal"),
    ];

    private static string ThemeCss(string theme) => theme switch
    {
        "moderno" => """
              h1 { font-size: 23pt; color: #12305c; }
              h2 { text-transform: none; letter-spacing: 0; font-size: 11pt; color: #12305c; border-bottom: 0; border-left: 3pt solid #12305c; padding: 0 0 0 6pt; }
            """,
        "compacto" => """
              @page { size: A4; margin: 10mm; }
              body { font-size: 9.6pt; line-height: 1.32; }
              h1 { font-size: 17pt; } h2 { margin: 8pt 0 3pt; } .role { margin-top: 5pt; } li { margin: 1pt 0; }
            """,
        "serifado" => """
              body { font-family: "Liberation Serif", "DejaVu Serif", Georgia, serif; font-size: 10.8pt; }
              h1 { font-weight: 600; letter-spacing: 0; } h2 { font-variant: small-caps; text-transform: none; letter-spacing: 0.04em; font-size: 11pt; }
            """,
        _ => "",
    };

    public static string Render(Draft d, Selection s, string language, string theme = "classico")
    {
        var en = language == "en";
        string T(string pt, string english) => en ? english : pt;
        static string E(string? x) => WebUtility.HtmlEncode(x ?? "");
        string Dates(ProfileFact f) => $"{Month(f.Start, en)} - {(f.End is null ? T("atual", "Present") : Month(f.End, en))}";

        var sb = new StringBuilder();
        sb.Append($$$"""
            <!doctype html><html lang="{{{(en ? "en" : "pt-BR")}}}"><head><meta charset="utf-8"><title>{{NOME}} - {{{(en ? "Resume" : "Currículo")}}}</title>
            <style>
              @page { size: A4; margin: 14mm; }
              body { font-family: "Inter", "Liberation Sans", "DejaVu Sans", sans-serif; color: #16181d; font-size: 10.3pt; line-height: 1.42; margin: 0; }
              h1 { font-size: 20pt; margin: 0 0 2pt; letter-spacing: -0.01em; }
              .headline { font-size: 11pt; color: #3a3f4b; margin: 0 0 4pt; }
              .contact { font-size: 9.3pt; color: #3a3f4b; margin: 0 0 10pt; }
              h2 { font-size: 9.6pt; text-transform: uppercase; letter-spacing: 0.08em; color: #3a3f4b; border-bottom: 1px solid #d5d8de; padding-bottom: 2pt; margin: 12pt 0 5pt; }
              .role { display: flex; justify-content: space-between; gap: 12pt; font-weight: 600; margin-top: 7pt; }
              .dates { font-weight: 400; color: #3a3f4b; white-space: nowrap; }
              .tech { color: #3a3f4b; font-size: 9.2pt; margin: 1pt 0 2pt; }
              ul { margin: 2pt 0 0 14pt; padding: 0; } li { margin: 1.5pt 0; }
              p { margin: 0; }
            {{{ThemeCss(theme)}}}
            </style></head><body>
            <h1>{{NOME}}</h1>
            """);
        if (d.Headline is not null) sb.Append($"""<p class="headline">{E(d.Headline.Text)}</p>""");
        sb.Append("""<p class="contact">{{CONTATO}}</p>""");
        if (d.Summary is not null) sb.Append($"<h2>{T("Resumo", "Summary")}</h2><p>{E(d.Summary.Text)}</p>");
        // Competências logo depois do resumo: é onde o recrutador e o ATS procuram as palavras da vaga primeiro.
        if (d.Skills.Length > 0) sb.Append($"<h2>{T("Competências", "Skills")}</h2><p>{E(string.Join(", ", d.Skills))}</p>");
        if (d.Experiences.Length > 0) sb.Append($"<h2>{T("Experiência", "Experience")}</h2>");
        foreach (var e in d.Experiences)
        {
            var job = s.Get(e.Fact)!.Fact;
            var role = job.Organization is { Length: > 0 } org && !e.Title.Contains(org, StringComparison.OrdinalIgnoreCase) ? $"{e.Title}, {org}" : e.Title;
            sb.Append($"""<div class="role"><span>{E(role)}</span><span class="dates">{E(Dates(job))}</span></div>""");
            if (job.Technologies.Count > 0) sb.Append($"""<div class="tech">{E(string.Join(", ", job.Technologies))}</div>""");
            if (e.Bullets.Length > 0) sb.Append("<ul>").Append(string.Concat(e.Bullets.Select(b => $"<li>{E(b.Text)}</li>"))).Append("</ul>");
        }
        if (d.Education.Length > 0) sb.Append($"<h2>{T("Formação", "Education")}</h2><ul>{string.Concat(d.Education.Select(x => $"<li>{E(x.Text)}{Year(x.Text, s.Get(x.Fact)?.Fact)}</li>"))}</ul>");
        var certs = s.Of("certification").ToList();
        if (certs.Count > 0) sb.Append($"<h2>{T("Certificações", "Certifications")}</h2><ul>{string.Concat(certs.Select(c => $"<li>{E(c.Fact.Statement)}</li>"))}</ul>");
        if (d.Languages.Length > 0) sb.Append($"<h2>{T("Idiomas", "Languages")}</h2><p>{E(string.Join(", ", d.Languages.Select(x => x.Text)))}</p>");
        sb.Append("</body></html>");
        return sb.ToString();

        static string Year(string text, ProfileFact? f) => f?.End is { Length: >= 4 } y && !text.Contains(y[..4], StringComparison.Ordinal) ? $" ({y[..4]})" : "";
    }

    /// <summary>O único lugar onde dados pessoais entram: na hora de gerar o PDF.</summary>
    public static string Fill(string html, Identity id)
    {
        static string E(string? x) => WebUtility.HtmlEncode(x ?? "");
        var contact = string.Join(" | ", new[] { id.Location, id.Email, id.Phone, Short(id.LinkedIn), Short(id.GitHub), Short(id.Website) }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(E));
        return html.Replace("{{NOME}}", E(id.FullName)).Replace("{{CONTATO}}", contact);

        // Endereço sem o perfil (linkedin.com/in sem o nome, github.com sozinho) não entra: no papel parece erro.
        static string? Short(string? url)
        {
            var s = url?.Replace("https://", "").Replace("http://", "").Replace("www.", "").TrimEnd('/');
            if (string.IsNullOrWhiteSpace(s)) return null;
            var profile = s.StartsWith("linkedin.com", StringComparison.OrdinalIgnoreCase) || s.StartsWith("github.com", StringComparison.OrdinalIgnoreCase);
            return profile && (!s.Contains('/') || s.EndsWith("/in", StringComparison.OrdinalIgnoreCase)) ? null : s;
        }
    }

    private static string Month(string? ym, bool en)
    {
        if (string.IsNullOrWhiteSpace(ym)) return "";
        if (ym.Length < 7) return ym[..4];
        var m = int.Parse(ym[5..7]);
        string[] pt = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];
        string[] english = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
        return $"{(en ? english : pt)[m - 1]} {ym[..4]}";
    }
}
