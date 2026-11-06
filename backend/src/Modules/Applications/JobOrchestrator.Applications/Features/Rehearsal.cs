using System.Diagnostics;
using System.Text.Json;
using JobOrchestrator.Applications.Features.Workflow;
using JobOrchestrator.Jobs.Contracts;
using JobOrchestrator.Materials.Contracts;

namespace JobOrchestrator.Applications.Features;

/// <summary>Um formulário do ensaio: o que o preenchedor fez na página da vaga, sem enviar.</summary>
internal sealed record RehearsalItem(Guid JobId, string Company, string Title, string Url, string? Eligibility, string Kind, string? Reason, string? Detail,
    string? Adapter, IReadOnlyList<FieldValue> Fields, double Seconds);

internal sealed record RehearsalView(DateTime StartedAt, DateTime? FinishedAt, bool Running, int Total, string? Error, IReadOnlyList<RehearsalItem> Items);

internal sealed record RehearsalInput(int? Limit);

/// <summary>
/// Ensaio dos formulários: para cada vaga da fila com formulário de ATS, abre a página, preenche com o seu perfil e o currículo base
/// e lê de volta, sem clicar em enviar (é o mesmo preparo da candidatura de verdade). O resultado diz, vaga por vaga, se o envio
/// sairia sozinho ou o que falta: pergunta sem resposta, CAPTCHA, login, declaração ou vaga fora do ar.
/// </summary>
internal sealed class Rehearsal(IServiceScopeFactory scopes, TimeProvider time, ILogger<Rehearsal> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string FilePath => Path.Combine(AppPaths.Data, "rehearsal.json");
    private int _running;

    public bool Running => Volatile.Read(ref _running) == 1;
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public RehearsalView? Last()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<RehearsalView>(File.ReadAllText(FilePath), Json) : null; }
        catch (Exception ex) when (ex is JsonException or IOException) { return null; }
    }

    public bool Start(int limit)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        _ = Task.Run(async () =>
        {
            try { await RunAsync(Math.Clamp(limit, 1, 40)); }
            catch (Exception ex)
            {
                log.LogError(ex, "Form rehearsal failed");
                Save(new RehearsalView(Now, Now, false, 0, ex.Message, []));
            }
            finally { Interlocked.Exchange(ref _running, 0); }
        });
        return true;
    }

    private async Task RunAsync(int limit)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var jobs = sp.GetRequiredService<IJobsApi>();
        var automation = sp.GetRequiredService<IAutomationApi>();
        var bundles = sp.GetRequiredService<Bundles>();
        var started = Now;
        var targets = await jobs.QueueAsync("ats", limit, CancellationToken.None);
        var items = new List<RehearsalItem>();
        Save(new RehearsalView(started, null, true, targets.Count, null, items));
        // O anexo do ensaio é o currículo base, sem IA e com o perfil de agora, no idioma da vaga como no envio de verdade
        // ("o currículo está em inglês?" responde certo). Ele só é testado no campo de arquivo, nunca enviado.
        var theme = sp.GetRequiredService<IConfiguration>().GetValue("Materials:Theme", "classico")!;
        var materials = sp.GetRequiredService<IMaterialsApi>();
        var pt = await materials.ComposeBaseAsync("pt", theme, CancellationToken.None);
        var en = await materials.ComposeBaseAsync("en", theme, CancellationToken.None);
        if (pt is not { Ok: true, PdfPath: { } ptPdf } || en is not { Ok: true, PdfPath: { } enPdf })
        {
            Save(new RehearsalView(started, Now, false, targets.Count, pt.Error ?? en.Error ?? "O currículo base não ficou pronto.", items));
            return;
        }
        foreach (var t in targets)
        {
            var clock = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            RehearsalItem item;
            try
            {
                var text = await jobs.TextAsync(t.Id, cts.Token);
                var pdf = Profile.Contracts.Languages.Detect($"{text?.Title} {text?.Description}") == "en" ? enPdf : ptPdf;
                var bundle = await bundles.ForJobAsync(t.Id, pdf, cts.Token);
                if (bundle is null)
                {
                    Save(new RehearsalView(started, Now, false, targets.Count, "Envie e confirme o currículo antes do ensaio.", items));
                    return;
                }
                var outcome = await automation.PrepareAsync(new ApplyTarget(Guid.CreateVersion7(), t.ApplyUrl), bundle, cts.Token);
                item = new(t.Id, t.Company, t.Title, t.ApplyUrl, t.Eligibility, outcome.Kind.ToString(), outcome.ReasonCode, outcome.Detail, outcome.Value?.AdapterId,
                    outcome.Value?.Fields ?? [], Math.Round(clock.Elapsed.TotalSeconds, 1));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Uma página que trava ou cai não para o ensaio: fica registrada e o resto segue.
                log.LogWarning(ex, "Rehearsal of {Url} failed", t.ApplyUrl);
                item = new(t.Id, t.Company, t.Title, t.ApplyUrl, t.Eligibility, "Error", cts.IsCancellationRequested ? "timeout" : "error",
                    cts.IsCancellationRequested ? "A página não terminou de carregar em 90 segundos." : ex.Message, null, [], Math.Round(clock.Elapsed.TotalSeconds, 1));
            }
            items.Add(item);
            Save(new RehearsalView(started, null, true, targets.Count, null, items));
        }
        Save(new RehearsalView(started, Now, false, targets.Count, null, items));
    }

    private static void Save(RehearsalView view)
    {
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(view, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}

internal sealed class RehearsalEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/applications/rehearsal").WithTags("Applications");
        g.MapGet("", (Rehearsal r) => Results.Ok(new { running = r.Running, last = r.Last() }));
        g.MapPost("", (RehearsalInput? body, Rehearsal r) =>
            r.Start(body?.Limit ?? 25) ? Results.Accepted() : Results.Problem("O ensaio já está rodando.", statusCode: 409));
    }
}
