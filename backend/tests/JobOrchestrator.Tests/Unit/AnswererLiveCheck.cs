using System.Net.Http.Json;
using System.Text.Json;
using JobOrchestrator.Applications.Features.Workflow;
using JobOrchestrator.Jobs.Contracts;
using JobOrchestrator.Profile.Contracts;
using Xunit.Abstractions;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Opt-in: runs only with JO_AI_CHECK pointing at a profile as /api/profile returns it.</summary>
public sealed class AiCheckFactAttribute : FactAttribute
{
    public AiCheckFactAttribute() { if (Environment.GetEnvironmentVariable("JO_AI_CHECK") is not { Length: > 0 }) Skip = "Checagem com modelo real: rode com JO_AI_CHECK=<perfil.json>."; }
}

/// <summary>
/// The real local model (Ollama, JO_AI_CHECK_MODEL, qwen3:4b by default: the Mac's fallback) answers questions that stopped real forms,
/// from a real profile, through the same prompt, schema and check the app uses. Prints every answer and why a refused one was refused.
/// </summary>
public sealed class AnswererLiveCheck(ITestOutputHelper output)
{
    [AiCheckFact]
    public async Task The_local_model_answers_real_form_questions_from_the_facts()
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("JO_AI_CHECK")!));
        var web = JsonSerializerOptions.Web;
        var facts = doc.RootElement.GetProperty("facts").EnumerateArray()
            .Where(f => f.GetProperty("kind").GetString() != "headline")
            .Select((f, i) => ($"f{i + 1}", new ProfileFact(f.GetProperty("id").GetGuid(), f.GetProperty("kind").GetString()!, f.GetProperty("statement").GetString()!, null,
                f.GetProperty("organization").GetString(), f.GetProperty("role").GetString(), f.GetProperty("start").GetString(), f.GetProperty("end").GetString(),
                f.GetProperty("technologies").Deserialize<string[]>(web) ?? [], f.GetProperty("level").GetString(), ""))).Take(90).ToList();
        var conf = doc.RootElement.GetProperty("confirmations").Deserialize<Confirmations>(web)!;
        FormQuestion[] questions =
        [
            new("Qual é a sua expectativa salarial mensal em R$ para atuar como PJ?", "text", []),
            new("Você tem experiência prática de no mínimo 2 anos com .NET WebForms, incluindo a manutenção de componentes e ciclo de vida da página?", "radio", ["Sim", "Não"]),
            new("Walk us through a feature you owned end-to-end. What was your role from discovery to post-release?", "textarea", []),
            new("How are you currently using AI tools in your development workflow, and how do you decide when to trust the output?", "textarea", []),
            new("Describe your experience working across the front end and back end. Where do you feel strongest, and where are you still growing?", "textarea", []),
            new("Which JavaScript framework or library have you worked with extensively? (You can choose more than one)", "checkboxes", ["React", "Vue", "Angular", "Svelte", "None of the above"]),
            new("I confirm I am currently based in Latin America", "confirm", []),
        ];
        var job = new JobText { Title = "Senior Full Stack Software Engineer", Company = "Invicti Security", Location = "Remote, LATAM", Description = "We build application security products with .NET, C#, React and AWS." };
        var sources = Answerer.Sources(facts, conf, job.Company);
        var model = Environment.GetEnvironmentVariable("JO_AI_CHECK_MODEL") ?? "qwen3:4b";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var response = await http.PostAsJsonAsync("http://127.0.0.1:11434/api/chat", new Dictionary<string, object>
        {
            ["model"] = model, ["stream"] = false, ["think"] = false, ["options"] = new { temperature = 0 },
            ["format"] = JsonDocument.Parse(Answerer.Schema).RootElement,
            ["messages"] = new[] { new { role = "system", content = Answerer.System }, new { role = "user", content = Answerer.Prompt(job, conf, facts, questions, null, Answerer.Country(doc.RootElement.GetProperty("identity").GetProperty("location").GetString())) } },
        });
        using var reply = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var json = reply.RootElement.GetProperty("message").GetProperty("content").GetString()!;
        var answers = Answerer.Parse(json);
        var results = Answerer.Check(questions, answers, facts, sources);
        output.WriteLine($"{model}: {clock.Elapsed.TotalSeconds:0} s, {results.Count(r => r.Value is not null)} de {questions.Length} aceitas");
        foreach (var r in results)
            output.WriteLine($"- [{r.Question.Kind}] {r.Question.Label}\n    {(r.Value is not null ? "ACEITA: " + r.Value.Replace("\n", " / ") : "RECUSADA: " + r.Problem + " | resposta: " + answers.FirstOrDefault(a => a.Index == Array.IndexOf(questions, r.Question))?.Answer)}\n    fatos: {string.Join(" | ", r.Basis.Take(2))}");
        Assert.Equal(questions.Length, answers.Select(a => a.Index).Distinct().Count());
        Assert.Contains(results, r => r.Question.Kind == "radio" && r.Value is not null);
    }
}
