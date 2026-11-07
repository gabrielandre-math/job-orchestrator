using System.Text.RegularExpressions;
using JobOrchestrator.Automation.Contracts;
using JobOrchestrator.Jobs.Contracts;
using JobOrchestrator.Profile.Contracts;

namespace JobOrchestrator.Applications.Features.Workflow;

/// <summary>
/// Pacote do formulário a partir do perfil verdadeiro (módulo Profile): dados pessoais e as respostas do banco para esta vaga.
/// Anos com cada tecnologia saem das datas das experiências; pretensão sai do piso do vínculo e da moeda da vaga.
/// </summary>
internal sealed partial class Bundles(IProfileApi profiles, IJobsApi jobs, ApplicationsDbContext db)
{
    public Task<MaterialBundle?> ForAsync(JobApplication app, CancellationToken ct) => ForAsync(app, false, ct);

    /// <summary>
    /// The application's bundle: the job's bundle plus the answers the AI wrote from your profile for this form (yours still win), and
    /// whether your authorization accepts, in your name, the privacy and terms boxes.
    /// </summary>
    public async Task<MaterialBundle?> ForAsync(JobApplication app, bool acceptDeclarations, CancellationToken ct)
    {
        if (await ForJobAsync(app.JobId, app.ResumePath!, ct) is not { } bundle) return null;
        var drafted = await db.FormAnswers.AsNoTracking().Where(f => f.ApplicationId == app.Id && f.Accepted).Select(f => new { f.Question, f.Answer }).ToListAsync(ct);
        if (drafted.Count == 0) return bundle with { AcceptDeclarations = acceptDeclarations };
        var answers = new Dictionary<string, string>(bundle.Answers, StringComparer.Ordinal);
        foreach (var d in drafted) answers[CustomAnswer.AiKey(d.Question)] = d.Answer;
        return bundle with { Answers = answers, AcceptDeclarations = acceptDeclarations };
    }

    /// <summary>O mesmo pacote para uma vaga da fila, com o currículo dado (o ensaio dos formulários usa o base).</summary>
    public async Task<MaterialBundle?> ForJobAsync(Guid jobId, string resumePath, CancellationToken ct)
    {
        var p = await profiles.CurrentAsync(ct);
        if (p is null) return null;
        var job = await jobs.TextAsync(jobId, ct);
        var techs = p.Facts.SelectMany(f => f.Technologies).Concat(p.Facts.Where(f => f.Kind == "skill").Select(f => f.Statement)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var text = $"{job?.Title} {job?.Description}";
        // Remote work the eligibility check confirmed open to people living in Brazil is done from Brazil.
        var country = Country(job?.Location, job?.SalaryCurrency) ?? (await jobs.EligibilityAsync(jobId, ct) == "BR_ELIGIBLE_CONFIRMED" ? "BR" : null);
        var context = new AnswerContext(job?.Contract, job?.SalaryCurrency, country, techs, Languages.PtOrEn(text), job?.Source ?? "manual");
        // O banco do perfil e, por cima, as suas respostas às perguntas que ele não cobre.
        var answers = new Dictionary<string, string>(await profiles.AnswersAsync(context, ct), StringComparer.Ordinal);
        foreach (var (key, saved) in AnswerStore.Load()) answers[key] = saved.Answer;
        var id = p.Identity;
        return new MaterialBundle(id.FirstName, id.LastName, id.Email ?? "", id.Phone ?? "", id.LinkedIn ?? "", resumePath, answers);
    }

    /// <summary>
    /// The one country the job is in, or null when that is unknown: no country, several of them, or remote anywhere. Unknown never
    /// borrows the Brazilian authorization; the work authorization question is then left for you.
    /// </summary>
    internal static string? Country(string? location, string? currency)
    {
        var where = location ?? "";
        var found = SourceCatalog.MatchCountries(where).ToList();
        // The catalog skips two-letter aliases, so "US" is looked for here; "US or Canada" is then two countries, not Canada.
        if (Us().IsMatch(where) && !found.Contains("US")) found.Add("US");
        if (found.Count > 0) return found.Count == 1 ? found[0] : null;
        return string.Equals(currency, "BRL", StringComparison.OrdinalIgnoreCase) || Uf().IsMatch(where) ? "BR" : null;
    }

    [GeneratedRegex(@"\bUS\b|U\.S\.|\((?i:us)\)")]
    private static partial Regex Us();

    // A Brazilian state after the city ("Jundiaí - SP", "Natal/RN"), only the codes that are not also a US state or a country code.
    [GeneratedRegex(@"[,/\-]\s*(SP|RJ|MG|DF|CE|GO|PB|PI|RN|AP|RR|AC)\s*$")]
    private static partial Regex Uf();
}
