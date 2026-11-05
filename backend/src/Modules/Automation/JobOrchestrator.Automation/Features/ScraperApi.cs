using System.Diagnostics;
using JobOrchestrator.Intelligence.Contracts;

namespace JobOrchestrator.Automation.Features;

internal sealed record ScraperStatus(bool Installed, string? Version, string Venv, string Project, double CacheMb, bool Installing, string? InstallLog,
    int MaxPages, int MaxSeconds, int MaxAiPages, string Assist, bool CollectEmails);
internal sealed record ScraperSettings(int? MaxPages, int? MaxSeconds, int? MaxAiPages, string? Assist, bool? CollectEmails);
internal sealed record CrawlTest(string Seeds);
internal sealed record CrawlTestResult(int Found, IReadOnlyList<CrawlFound> Postings);
internal sealed record CrawlFound(string Title, string Company, string Location, string ApplyUrl, string? ApplyEmail, string Source);

/// <summary>
/// O coletor na tela: está instalado, instala num clique (ambiente virtual com o Scrapy, na sua pasta), guarda as escolhas
/// (páginas por site, tempo, IA de apoio, e-mails de candidatura) e faz uma varredura de teste ao vivo.
/// </summary>
internal sealed class ScraperApi(ScraperEnv env, IConfiguration config, IServiceProvider services, ILogger<ScraperApi> log)
{
    private static readonly Lock Gate = new();
    private static bool installing;
    private static string? installLog;

    public async Task<ScraperStatus> StatusAsync(CancellationToken ct)
    {
        string? version = null;
        if (env.Installed)
        {
            var info = new ProcessStartInfo(env.Scrapy, "version") { RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = env.Project };
            using var p = Process.Start(info)!;
            version = (await p.StandardOutput.ReadToEndAsync(ct)).Trim();
            await p.WaitForExitAsync(ct);
        }
        var cache = Directory.Exists(env.Cache) ? new DirectoryInfo(env.Cache).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1e6 : 0;
        lock (Gate)
            return new ScraperStatus(env.Installed, version, env.Venv, env.Project, Math.Round(cache, 1), installing, installLog,
                config.GetValue("Scraper:MaxPages", 60), config.GetValue("Scraper:MaxSeconds", 240), config.GetValue("Scraper:MaxAiPages", 8),
                config[$"Llm:Tasks:{LlmTasks.PageJobs.Name}:Rung"] ?? "Local", config.GetValue("Scraper:CollectEmails", true));
    }

    /// <summary>Cria o ambiente virtual e instala o projeto do coletor (Scrapy como dependência, pelo pip). Uma vez só.</summary>
    public void Install()
    {
        lock (Gate)
        {
            if (installing) return;
            installing = true;
            installLog = "Criando o ambiente do coletor…";
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Run("python3", ["-m", "venv", env.Venv], "Ambiente criado. Instalando o Scrapy…");
                await Run(Path.Combine(env.Venv, "bin", "pip"), ["install", "-q", "--disable-pip-version-check", "-e", env.Project], "Coletor instalado.");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Instalação do coletor falhou");
                lock (Gate) installLog = $"Falhou: {ex.Message}";
            }
            finally { lock (Gate) installing = false; }
        });
    }

    private static async Task Run(string file, string[] args, string done)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var err = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException((await err).Trim().Split('\n').LastOrDefault() ?? $"{file} saiu com {p.ExitCode}");
        lock (Gate) installLog = done;
    }

    public async Task SaveAsync(ScraperSettings s) => await UserSettings.SetAsync(new Dictionary<string, object?>
    {
        ["Scraper:MaxPages"] = s.MaxPages is { } m ? Math.Clamp(m, 5, 400) : null,
        ["Scraper:MaxSeconds"] = s.MaxSeconds is { } t ? Math.Clamp(t, 30, 1800) : null,
        ["Scraper:MaxAiPages"] = s.MaxAiPages is { } a ? Math.Clamp(a, 0, 40) : null,
        [$"Llm:Tasks:{LlmTasks.PageJobs.Name}:Rung"] = s.Assist is "Local" or "LocalStrong" or "Light" or "Medium" ? s.Assist : null,
        ["Scraper:CollectEmails"] = s.CollectEmails,
    });

    /// <summary>Varredura de teste agora, na API: o fio "crawl" aparece ao vivo no canvas e na tela.</summary>
    public async Task<CrawlTestResult> TestAsync(string seeds, CancellationToken ct)
    {
        var web = services.GetServices<ISourceConnector>().OfType<Adapters.WebSource>().First();
        var postings = await web.DiscoverAsync($"web:{seeds}", ct);
        return new CrawlTestResult(postings.Count, [.. postings.Take(30).Select(p => new CrawlFound(p.Title, p.Company, p.Location, p.ApplyUrl, p.ApplyEmail, p.Source))]);
    }
}

internal sealed class ScraperEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/scraper").WithTags("Automation");
        g.MapGet("status", async (ScraperApi api, CancellationToken ct) => Results.Ok(await api.StatusAsync(ct)));
        g.MapPost("install", (ScraperApi api) => { api.Install(); return Results.Accepted(); });
        g.MapPut("settings", async (ScraperSettings body, ScraperApi api) => { await api.SaveAsync(body); return Results.NoContent(); });
        g.MapPost("test", async (CrawlTest body, ScraperApi api, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(body.Seeds) ? Results.BadRequest() : Results.Ok(await api.TestAsync(body.Seeds.Trim(), ct)));
    }
}
