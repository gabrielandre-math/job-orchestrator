using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Jobs.Domain;

/// <summary>
/// A stack que a vaga pede, pelo texto: nomes canônicos (os mesmos da tela, com ícone quando a marca tem um oficial)
/// e os apelidos de cada um. A ordem é a da primeira menção, então o que a vaga cita primeiro vem primeiro.
/// </summary>
internal static partial class Stack
{
    private static readonly (string Name, string[] Aliases)[] Known =
    [
        ("C#", ["c#", "csharp", "c sharp"]), (".NET", [".net", "dotnet", ".net core", ".net 8", ".net 9", ".net 10"]), ("ASP.NET Core", ["asp.net core", "asp.net"]),
        ("Entity Framework", ["entity framework", "ef core"]), ("Blazor", ["blazor"]), ("Java", ["java"]), ("Kotlin", ["kotlin"]), ("Spring", ["spring boot", "spring"]),
        ("Go", ["golang", "go lang"]), ("Python", ["python"]), ("Django", ["django"]), ("Ruby", ["ruby"]), ("Rails", ["rails", "ruby on rails"]),
        ("PHP", ["php"]), ("Laravel", ["laravel"]), ("Node.js", ["node.js", "nodejs", "node"]), ("TypeScript", ["typescript"]), ("JavaScript", ["javascript"]),
        ("React", ["react", "react.js", "reactjs"]), ("Next.js", ["next.js", "nextjs"]), ("Angular", ["angular"]), ("Vue", ["vue", "vue.js", "vuejs"]), ("Svelte", ["svelte"]),
        ("Flutter", ["flutter"]), ("Dart", ["dart"]), ("Swift", ["swift"]), ("Android", ["android"]), ("iOS", ["ios"]), ("Rust", ["rust"]), ("Scala", ["scala"]), ("Elixir", ["elixir"]),
        ("AWS", ["aws", "amazon web services"]), ("Azure", ["azure"]), ("GCP", ["gcp", "google cloud"]), ("Kubernetes", ["kubernetes", "k8s"]), ("Docker", ["docker"]),
        ("Terraform", ["terraform"]), ("Kafka", ["kafka"]), ("RabbitMQ", ["rabbitmq", "rabbit mq"]), ("SQS", ["sqs"]), ("Lambda", ["lambda"]), ("DynamoDB", ["dynamodb"]),
        ("PostgreSQL", ["postgresql", "postgres"]), ("MySQL", ["mysql"]), ("SQL Server", ["sql server", "mssql"]), ("MongoDB", ["mongodb", "mongo"]), ("Redis", ["redis"]),
        ("Elasticsearch", ["elasticsearch", "elastic search", "opensearch"]), ("Snowflake", ["snowflake"]), ("Databricks", ["databricks"]), ("Spark", ["spark", "pyspark"]),
        ("Airflow", ["airflow"]), ("GraphQL", ["graphql"]), ("gRPC", ["grpc"]), ("Linux", ["linux"]), ("Nginx", ["nginx"]), ("Git", ["git"]), ("GitHub Actions", ["github actions"]),
        ("Jenkins", ["jenkins"]), ("Datadog", ["datadog"]), ("Grafana", ["grafana"]), ("Prometheus", ["prometheus"]), ("Firebase", ["firebase"]), ("Supabase", ["supabase"]),
        ("Tailwind", ["tailwind"]), ("Figma", ["figma"]), ("Microsserviços", ["microsserviços", "microsservicos", "microservices", "micro services"]),
        ("Mensageria", ["mensageria", "message broker", "event-driven", "event driven"]), ("CI/CD", ["ci/cd", "ci / cd", "continuous integration"]),
        ("DDD", ["ddd", "domain-driven", "domain driven"]), ("TDD", ["tdd"]), ("SOLID", ["solid"]), ("xUnit", ["xunit"]), ("Hangfire", ["hangfire"]),
    ];

    private static readonly Regex[] Patterns = [.. Known.Select(k => new Regex(
        @"(?<![\p{L}\p{N}#+.])(?:" + string.Join("|", k.Aliases.OrderByDescending(a => a.Length).Select(a => Regex.Escape(Fold(a)))) + @")(?![\p{L}\p{N}#+])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant))];

    public static List<string> Detect(string title, string description)
    {
        var text = Fold(title + "\n" + description);
        var found = new List<(string Name, int At)>();
        for (var i = 0; i < Known.Length; i++)
            if (Patterns[i].Match(text) is { Success: true } m) found.Add((Known[i].Name, m.Index));
        // "Go" e "Lambda" sozinhos são palavras comuns em inglês: só contam com outra tecnologia por perto.
        found.RemoveAll(f => f.Name is "Go" && !text.Contains("golang") && found.Count(x => x.Name is not "Go") < 2);
        found.RemoveAll(f => f.Name is "Lambda" && !text.Contains("aws"));
        return [.. found.OrderBy(f => f.At).Select(f => f.Name).Take(24)];
    }

    private static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}

/// <summary>
/// Domínio da empresa, para a logo: o site que a descrição cita (o que mais aparece, fora de ATS, redes e encurtadores),
/// ou o endereço da candidatura quando ele é da própria empresa.
/// </summary>
internal static partial class CompanyDomain
{
    private static readonly string[] Ignore =
    [
        "greenhouse.io", "lever.co", "ashbyhq.com", "smartrecruiters.com", "workable.com", "gupy.io", "linkedin.com", "github.com", "glassdoor.com", "indeed.com",
        "youtube.com", "instagram.com", "facebook.com", "twitter.com", "x.com", "medium.com", "bit.ly", "goo.gl", "forms.gle", "google.com", "notion.so", "notion.site",
        "remotive.com", "ycombinator.com", "news.ycombinator.com", "wellfound.com", "angel.co", "breezy.hr", "recruitee.com", "teamtailor.com", "jobs.ashbyhq.com"
    ];

    public static string? Find(string description, string applyUrl)
    {
        var hosts = Url().Matches(description).Select(m => Host(m.Value)).OfType<string>().Where(h => !Ignored(h)).ToList();
        var best = hosts.GroupBy(h => h).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        if (best is not null) return best;
        var apply = Host(applyUrl);
        return apply is not null && !Ignored(apply) ? apply : null;
    }

    private static bool Ignored(string host) => Ignore.Any(i => host == i || host.EndsWith("." + i, StringComparison.Ordinal));

    private static string? Host(string url)
    {
        var u = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
        if (!Uri.TryCreate(u.TrimEnd('.', ',', ')', ';'), UriKind.Absolute, out var uri) || !uri.Host.Contains('.')) return null;
        var host = uri.Host.ToLowerInvariant();
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 || host == "localhost") return null;
        foreach (var prefix in new[] { "www.", "jobs.", "careers.", "carreiras.", "vagas.", "trabalhe.", "boards." }) if (host.StartsWith(prefix, StringComparison.Ordinal)) host = host[prefix.Length..];
        return host.Length > 120 ? null : host;
    }

    [GeneratedRegex(@"(?:https?://|www\.)[^\s<>()""']+", RegexOptions.IgnoreCase)] private static partial Regex Url();
}
