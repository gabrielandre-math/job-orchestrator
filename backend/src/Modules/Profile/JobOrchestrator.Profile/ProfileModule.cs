using System.Reflection;

namespace JobOrchestrator.Profile;

/// <summary>Porta de entrada do módulo Profile: o perfil verdadeiro, fatos com evidência, a tela única e o banco de respostas.</summary>
public static class ProfileModule
{
    public static Assembly Assembly => typeof(ProfileModule).Assembly;

    public static IServiceCollection AddProfileModule(this IServiceCollection s, IConfiguration config)
    {
        s.AddModuleDatabase<ProfileDbContext>(config, "Profile");
        s.AddTransient(typeof(IPipelineBehavior<,>), typeof(ProfileTransaction<,>));
        s.AddScoped<IProfileApi, Features.ProfileApi>();
        return s;
    }

    public static IServiceCollection AddProfileWorker(this IServiceCollection s)
    {
        s.AddEventConsumer<Features.DocumentImportedConsumer>();
        return s.AddHostedService<OutboxPublisher<ProfileDbContext>>();
    }

    /// <summary>Migra e, sem perfil nenhum, monta o perfil de teste do arquivo em Profile:Seed (só desenvolvimento e testes).</summary>
    public static async Task MigrateProfileAsync(this IServiceProvider sp, IConfiguration config)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProfileDbContext>();
        await db.Database.MigrateAsync();
        if (config["Profile:Seed"] is not { Length: > 0 } path || !File.Exists(path) || await db.Profiles.AnyAsync()) return;
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        db.Profiles.Add(Seed.Build(path, now));
        await db.SaveChangesAsync();
    }
}

/// <summary>Perfil sintético a partir de um JSON (config/profile.example.json). Só para hosts de teste: o Guardião confere.</summary>
internal static class Seed
{
    private sealed record Experience(string Company, string Title, string Start, string? End, string[] Technologies, string[] Highlights);
    private sealed record Education(string Institution, string Degree, string? End);
    private sealed record File(string FirstName, string LastName, string Email, string Phone, string LinkedIn, string Headline, string Location, string Summary,
        Experience[] Experiences, Education[] Education, string[] Languages, Dictionary<string, string> Answers);

    public static CandidateProfile Build(string path, DateTime now)
    {
        var f = JsonSerializer.Deserialize<File>(System.IO.File.ReadAllText(path), JsonSerializerOptions.Web)!;
        var profile = CandidateProfile.Create(now, synthetic: true);
        var facts = new List<VerifiedFact>();
        var position = 0;
        VerifiedFact Add(string kind, string statement, Guid? parent = null, string? org = null, string? role = null, string? start = null, string? end = null,
            IEnumerable<string>? tech = null, string? level = null)
        {
            var fact = VerifiedFact.Create(profile.Id, null, kind, statement, FactStatus.Verified, position++, now, parent, org, role, start, end, tech, level, statement);
            facts.Add(fact);
            return fact;
        }
        Add("headline", f.Headline);
        Add("summary", f.Summary);
        foreach (var x in f.Experiences)
        {
            var job = Add("experience", $"{x.Title}, {x.Company}", null, x.Company, x.Title, x.Start, x.End, x.Technologies);
            foreach (var h in x.Highlights) Add("achievement", h, job.Id, tech: x.Technologies.Where(t => h.Contains(t, StringComparison.OrdinalIgnoreCase)));
        }
        foreach (var e in f.Education) Add("education", $"{e.Degree}, {e.Institution}", null, e.Institution, e.Degree, null, e.End);
        foreach (var l in f.Languages)
            Add("language", l, level: Claims.Fold(l) switch
            {
                var s when s.Contains("nativ") => "native", var s when s.Contains("fluent") => "fluent", var s when s.Contains("avanc") => "advanced",
                var s when s.Contains("intermed") => "intermediate", var s when s.Contains("basic") => "basic", _ => "unknown"
            });
        foreach (var t in f.Experiences.SelectMany(x => x.Technologies).Distinct(StringComparer.OrdinalIgnoreCase)) Add("skill", t);
        var english = f.Answers.GetValueOrDefault("english_level")?.ToLowerInvariant();
        var c = Confirmations.Empty with
        {
            AuthorizedCountries = f.Answers.GetValueOrDefault("work_authorization") == "Yes" ? ["BR"] : [],
            NeedsSponsorship = f.Answers.GetValueOrDefault("requires_sponsorship") == "Yes",
            EnglishLevel = english is "basic" or "intermediate" or "advanced" or "fluent" or "native" ? english : null,
            StartAvailability = f.Answers.GetValueOrDefault("notice_period"),
            Modalities = ["remote", "hybrid"]
        };
        profile.Seed(new Identity($"{f.FirstName} {f.LastName}", f.Email, f.Phone, f.Location, f.LinkedIn, null, null), facts, c, now);
        return profile;
    }
}
