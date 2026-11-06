using JobOrchestrator.Jobs.Contracts;
using JobOrchestrator.Profile.Contracts;

namespace JobOrchestrator.Applications.Features.Workflow;

/// <summary>
/// Pacote do formulário a partir do perfil verdadeiro (módulo Profile): dados pessoais e as respostas do banco para esta vaga.
/// Anos com cada tecnologia saem das datas das experiências; pretensão sai do piso do vínculo e da moeda da vaga.
/// </summary>
internal sealed class Bundles(IProfileApi profiles, IJobsApi jobs)
{
    public Task<MaterialBundle?> ForAsync(JobApplication app, CancellationToken ct) => ForJobAsync(app.JobId, app.ResumePath!, ct);

    /// <summary>O mesmo pacote para uma vaga da fila, com o currículo dado (o ensaio dos formulários usa o base).</summary>
    public async Task<MaterialBundle?> ForJobAsync(Guid jobId, string resumePath, CancellationToken ct)
    {
        var p = await profiles.CurrentAsync(ct);
        if (p is null) return null;
        var job = await jobs.TextAsync(jobId, ct);
        var techs = p.Facts.SelectMany(f => f.Technologies).Concat(p.Facts.Where(f => f.Kind == "skill").Select(f => f.Statement)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var text = $"{job?.Title} {job?.Description}";
        var context = new AnswerContext(job?.Contract, job?.SalaryCurrency, Country(job), techs, Languages.Detect(text), job?.Source ?? "manual");
        // O banco do perfil e, por cima, as suas respostas às perguntas que ele não cobre.
        var answers = new Dictionary<string, string>(await profiles.AnswersAsync(context, ct), StringComparer.Ordinal);
        foreach (var (key, saved) in AnswerStore.Load()) answers[key] = saved.Answer;
        var id = p.Identity;
        return new MaterialBundle(id.FirstName, id.LastName, id.Email ?? "", id.Phone ?? "", id.LinkedIn ?? "", resumePath, answers);
    }

    /// <summary>País da vaga pelo local: Brasil por padrão; EUA só quando a vaga diz.</summary>
    private static string Country(JobText? job)
    {
        var where = $"{job?.Location}".ToLowerInvariant();
        return where.Contains("united states") || where.Contains("usa") || where.Contains("u.s.") || where.Contains("(us)") ? "US" : "BR";
    }
}
