using JobOrchestrator.Automation.Adapters;
using JobOrchestrator.BuildingBlocks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Opt-in: runs only with JO_FORM_CHECK holding job page URLs, one per line.</summary>
public sealed class FormCheckFactAttribute : FactAttribute
{
    public FormCheckFactAttribute() { if (Environment.GetEnvironmentVariable("JO_FORM_CHECK") is not { Length: > 0 }) Skip = "Checagem em sites reais: rode com JO_FORM_CHECK=<urls>."; }
}

/// <summary>
/// The open-site form against real job pages, prepare only: it fills with a made-up person (example.com, a blank PDF) and never
/// clicks to send. Prints what each page did: the form filled, the questions left, or what stopped it.
/// </summary>
public sealed class GenericFormLiveCheck(ITestOutputHelper output)
{
    [FormCheckFact]
    public async Task The_open_site_form_prepares_real_pages_without_sending()
    {
        var dir = Directory.CreateTempSubdirectory("jo-form-check-").FullName;
        var pdf = Path.Combine(dir, "Resume-Test.pdf");
        await File.WriteAllTextAsync(pdf, "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[]/Count 0>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");
        var options = Options.Create(new AutomationOptions { Browser = new BrowserOptions { TimeoutSeconds = 20 } });
        await using var pool = new BrowserPool(options, NullLogger<BrowserPool>.Instance);
        var key = new GuardKey(Path.Combine(dir, "guard.key"));
        // The adapter the router would pick: the platform's own when there is one, the open-site form otherwise.
        LabelForm[] forms = [new WorkableForm(pool, new NoHttp(), key, TimeProvider.System, options), new GenericForm(pool, new NoHttp(), key, TimeProvider.System, options)];
        var bundle = new MaterialBundle("Teste", "Exemplo", "teste.exemplo@example.com", "+55 21 90000-0000", "https://www.linkedin.com/in/teste-exemplo", pdf,
            new Dictionary<string, string> { ["location"] = "Rio de Janeiro, RJ", ["salary_expectation"] = "R$ 6.000 por mês (PJ)", ["salary_pj"] = "R$ 6.000 por mês (PJ)",
                ["start_date"] = "Imediata", ["english_level"] = "Avançado" }, AcceptDeclarations: true);
        foreach (var url in Environment.GetEnvironmentVariable("JO_FORM_CHECK")!.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var uri = new Uri(url);
            var form = forms.OrderByDescending(f => f.Fit(uri)).First();
            output.WriteLine($"== {url}\n   {form.Id}, fit {form.Fit(uri)}");
            if (form.Fit(uri) == 0) continue;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var r = await form.PrepareAsync(new ApplyTarget(Guid.NewGuid(), url), bundle, CancellationToken.None);
                output.WriteLine($"   {r.Kind} {r.ReasonCode} em {clock.Elapsed.TotalSeconds:0} s: {r.Detail}");
                foreach (var f in r.Value?.Fields ?? []) output.WriteLine($"     preenchido: {f.Label} = {f.Value} ({f.Source})");
                foreach (var q in r.Questions ?? []) output.WriteLine($"     pergunta [{q.Kind}]: {q.Label}{(q.Options.Count > 0 ? " | " + string.Join(" / ", q.Options) : "")}");
            }
            catch (Exception ex) { output.WriteLine($"   erro: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
        }
    }

    private sealed class NoHttp : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
}
