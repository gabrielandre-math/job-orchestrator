using System.Text.RegularExpressions;

namespace JobOrchestrator.Materials.Compose;

/// <summary>Um fato escolhido para a vaga, com o rótulo curto (F1, F2…) que o modelo cita.</summary>
internal sealed record Picked(string Label, ProfileFact Fact, string? ParentLabel);

/// <summary>O que o Seletor separou: fatos, idioma do currículo e as tecnologias da vaga que você tem e que não tem.</summary>
internal sealed record Selection(IReadOnlyList<Picked> Facts, string Language, IReadOnlyList<string> Matched, IReadOnlyList<string> Missing)
{
    public Picked? Get(string label) => Facts.FirstOrDefault(f => f.Label == label);
    public IEnumerable<Picked> Of(string kind) => Facts.Where(f => f.Fact.Kind == kind);
    public IEnumerable<Picked> Children(string label) => Facts.Where(f => f.ParentLabel == label);
}

/// <summary>
/// Agente Seletor (sem IA): escolhe os fatos que importam para a vaga. Experiências mais recentes primeiro; dentro de cada uma,
/// as conquistas que citam o que a vaga pede vêm na frente. Idioma do currículo segue o idioma da vaga.
/// </summary>
internal static partial class Selector
{
    // Tecnologias comuns em vagas, para dizer o que a vaga pede e o perfil não tem. Não entram no currículo.
    private static readonly string[] Known =
    [
        "C#", ".NET", "ASP.NET", "Java", "Kotlin", "Go", "Golang", "Python", "Ruby", "PHP", "Node.js", "TypeScript", "JavaScript", "React", "Angular", "Vue",
        "AWS", "Azure", "GCP", "Kubernetes", "Docker", "Terraform", "Kafka", "RabbitMQ", "SQS", "PostgreSQL", "MySQL", "SQL Server", "MongoDB", "Redis",
        "Elasticsearch", "GraphQL", "gRPC", "Microservices", "Microsserviços", "CI/CD", "Linux", "Spark", "Airflow", "Rust", "Scala", "Elixir", "Flutter"
    ];

    public static Selection Pick(ProfileSnapshot p, JobText job)
    {
        var text = $"{job.Title}\n{job.Description}";
        var folded = Claims.Fold(text);
        var mine = p.Facts.Where(f => f.Kind == "skill").Select(f => f.Statement).Concat(p.Facts.SelectMany(f => f.Technologies))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var matched = mine.Where(t => Mentions(folded, t)).ToList();
        var missing = Known.Where(t => Mentions(folded, t) && !mine.Any(m => Claims.Fold(m) == Claims.Fold(t) || Claims.Fold(m).Contains(Claims.Fold(t)))).ToList();
        int Score(ProfileFact f) => matched.Count(t => Mentions(Claims.Fold(f.Statement + " " + string.Join(' ', f.Technologies)), t));

        var picked = new List<Picked>();
        var n = 0;
        string Next() => $"F{++n}";
        foreach (var f in p.Facts.Where(f => f.Kind is "headline" or "summary")) picked.Add(new(Next(), f, null));
        var jobs = p.Facts.Where(f => f.Kind == "experience").OrderByDescending(f => f.End ?? "9999").ThenByDescending(f => f.Start ?? "").Take(5).ToList();
        for (var i = 0; i < jobs.Count; i++)
        {
            var label = Next();
            picked.Add(new(label, jobs[i], null));
            var take = i < 2 ? 5 : 3;
            foreach (var a in p.Facts.Where(f => f.Kind == "achievement" && f.ParentId == jobs[i].Id).OrderByDescending(Score).Take(take))
                picked.Add(new(Next(), a, label));
        }
        foreach (var f in p.Facts.Where(f => f.Kind == "skill").OrderByDescending(Score).ThenBy(f => f.Statement)) picked.Add(new(Next(), f, null));
        foreach (var f in p.Facts.Where(f => f.Kind == "education").OrderByDescending(f => f.End ?? "9999").Take(3)) picked.Add(new(Next(), f, null));
        foreach (var f in p.Facts.Where(f => f.Kind == "language")) picked.Add(new(Next(), f, null));
        foreach (var f in p.Facts.Where(f => f.Kind == "certification").OrderByDescending(Score).Take(4)) picked.Add(new(Next(), f, null));
        return new Selection(picked, Language(job.Description + " " + job.Title), matched, missing);
    }

    internal static string Language(string text) => Languages.Detect(text);

    private static bool Mentions(string folded, string term)
    {
        var t = Claims.Fold(term);
        if (t.Length == 0) return false;
        var before = t.StartsWith('.') ? "" : @"(?<![\p{L}\p{N}])";
        return Regex.IsMatch(folded, before + Regex.Escape(t) + @"(?![\p{L}\p{N}#+])");
    }

}
