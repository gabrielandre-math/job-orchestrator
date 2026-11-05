using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

// ATS falso no formato do Greenhouse, para testar sem tocar em site real.
// Cenários por vaga: normal, captcha, confirmação lenta (registra e demora), pergunta nova, vaga encerrada, duplicada em outro board.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("FAKEATS_URL") ?? "http://127.0.0.1:5470");
var app = builder.Build();
var baseUrl = (Environment.GetEnvironmentVariable("FAKEATS_URL") ?? "http://127.0.0.1:5470").TrimEnd('/');
var statePath = Environment.GetEnvironmentVariable("FAKEATS_STATE") ?? Path.Combine(Path.GetTempPath(), "fake-ats-state.json");
var slowSeconds = int.TryParse(Environment.GetEnvironmentVariable("FAKEATS_SLOW_SECONDS"), out var s) ? s : 40;
var store = Store.Load(statePath);

Job[] jobs =
[
    new(101, "Senior Backend Engineer (.NET)", "Norte Pagamentos", "Remote (LATAM)", "normal",
        "We build payment APIs in C# and .NET 10 with ASP.NET Core, PostgreSQL and AWS (SQS, Lambda). Remote across Latin America."),
    new(102, "Software Engineer, Platform (C#/.NET)", "Atlas Saúde", "Remote, Brazil", "captcha",
        "Platform team working with C#, .NET, AWS and Kafka. Fully remote for people based in Brazil."),
    new(103, "Backend Developer .NET", "Ponte Logística", "São Paulo, Brazil (Remote)", "slow",
        "Backend services in C# and .NET with SQL Server and PostgreSQL. Remote with occasional visits."),
    new(104, "Go Engineer", "Rota Digital", "Remote", "normal",
        "Go services on Kubernetes. Experience with gRPC and Postgres."),
    new(105, "Senior .NET Engineer", "Harbor Inc.", "Remote (US only)", "normal",
        "C# and .NET on AWS. US only: must reside in the US."),
    new(106, "Backend Engineer .NET", "Quadra Tech", "Remote, Brazil", "closed",
        "C#, .NET and AWS. This position is no longer accepting applications."),
    new(108, "Full Stack Engineer (.NET + React)", "Lume Educação", "Remote, Brazil", "question",
        "Full stack with C#, .NET, ASP.NET Core, React and AWS. Remote in Brazil.")
];
var byId = jobs.ToDictionary(j => j.Id);

object Board(IEnumerable<Job> list) => new
{
    jobs = list.Select(j => new
    {
        id = j.Id, title = j.Title, company_name = j.Company, updated_at = "2026-11-04T12:00:00Z",
        location = new { name = j.Location }, absolute_url = $"{baseUrl}/jobs/{j.Id}/apply", content = WebUtility.HtmlEncode($"<p>{j.Description}</p>")
    })
};

app.MapGet("/boards/demo", () => Results.Ok(new { name = "Demo Board" }));
app.MapGet("/boards/demo/jobs", () => Results.Ok(Board(jobs)));
// Um agregador que repete a vaga 101 (mesmo link): o orquestrador tem que tratar como duplicada.
app.MapGet("/boards/agregador", () => Results.Ok(new { name = "Agregador" }));
app.MapGet("/boards/agregador/jobs", () => Results.Ok(Board(jobs.Where(j => j.Id == 101))));

app.MapGet("/jobs/{id:int}/apply", (int id) =>
{
    if (!byId.TryGetValue(id, out var job) || job.Scenario == "closed") return Results.NotFound();
    return Results.Content(Html.Form(job), "text/html; charset=utf-8");
});

app.MapPost("/jobs/{id:int}/apply", async (int id, HttpRequest request) =>
{
    if (!byId.TryGetValue(id, out var job) || job.Scenario == "closed") return Results.NotFound();
    var form = await request.ReadFormAsync();
    var email = form["email"].ToString().Trim().ToLowerInvariant();
    var missing = new[] { "first_name", "last_name", "email", "q_years", "q_auth", "q_sponsor" }.Where(f => string.IsNullOrWhiteSpace(form[f])).ToList();
    if (form.Files["resume"] is not { Length: > 0 }) missing.Add("resume");
    if (missing.Count > 0) return Results.Content(Html.Page($"<div role=\"alert\">Please fill: {string.Join(", ", missing)}</div>"), "text/html; charset=utf-8");
    if (store.Find(id, email) is not null) return Results.Content(Html.Page("<div role=\"alert\">You have already applied to this job.</div>"), "text/html; charset=utf-8");
    var receipt = store.Add(id, email);
    if (job.Scenario == "slow") await Task.Delay(TimeSpan.FromSeconds(slowSeconds));
    return Results.Content(Html.Page($"<h1>Thank you for applying to {WebUtility.HtmlEncode(job.Company)}</h1><p>Application ID: {receipt}</p>"), "text/html; charset=utf-8");
});

// Portal do candidato (só leitura): usado pela reconciliação.
app.MapGet("/api/receipts", (int job, string email) =>
    store.Find(job, email.Trim().ToLowerInvariant()) is { } r ? Results.Ok(new { receiptId = r.ReceiptId, at = r.At }) : Results.NotFound());
app.MapGet("/api/submissions", () => Results.Ok(store.All()));
app.MapPost("/api/reset", () => { store.Reset(); return Results.NoContent(); });
app.Run();

internal sealed record Job(int Id, string Title, string Company, string Location, string Scenario, string Description);
internal sealed record Submission(int Job, string Email, string ReceiptId, DateTime At);

/// <summary>Candidaturas recebidas, gravadas em arquivo para sobreviver a reinícios do orquestrador.</summary>
internal sealed class Store(string path, List<Submission> items)
{
    private readonly Lock _gate = new();
    public static Store Load(string path) =>
        new(path, File.Exists(path) ? JsonSerializer.Deserialize<List<Submission>>(File.ReadAllText(path)) ?? [] : []);
    public Submission? Find(int job, string email) { lock (_gate) return items.FirstOrDefault(i => i.Job == job && i.Email == email); }
    public IReadOnlyList<Submission> All() { lock (_gate) return [.. items]; }
    public string Add(int job, string email)
    {
        lock (_gate)
        {
            var receipt = $"GH-{job}-{items.Count + 1:D4}";
            items.Add(new Submission(job, email, receipt, DateTime.UtcNow));
            File.WriteAllText(path, JsonSerializer.Serialize(items));
            return receipt;
        }
    }
    public void Reset() { lock (_gate) { items.Clear(); File.WriteAllText(path, "[]"); } }
}

internal static class Html
{
    public static string Page(string body) => $$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Fake ATS</title>
        <style>body{font-family:sans-serif;max-width:640px;margin:40px auto;color:#222}label{display:block;margin-top:12px;font-weight:600}input,select,textarea{width:100%;padding:6px}</style>
        </head><body>{{body}}</body></html>
        """;

    public static string Form(Job job)
    {
        var extra = job.Scenario switch
        {
            "captcha" => "<div class=\"g-recaptcha\" data-sitekey=\"fake-site-key\"></div>",
            "question" => "<label for=\"q_pattern\">Describe your favorite design pattern *</label><textarea id=\"q_pattern\" name=\"q_pattern\" required></textarea>",
            _ => ""
        };
        return Page($"""
            <h1>{WebUtility.HtmlEncode(job.Title)}</h1><p>{WebUtility.HtmlEncode(job.Company)}, {WebUtility.HtmlEncode(job.Location)}</p>
            <form method="post" enctype="multipart/form-data" action="/jobs/{job.Id}/apply">
              <label for="first_name">First Name *</label><input id="first_name" name="first_name" required>
              <label for="last_name">Last Name *</label><input id="last_name" name="last_name" required>
              <label for="email">Email *</label><input id="email" name="email" type="email" required>
              <label for="phone">Phone</label><input id="phone" name="phone">
              <label for="resume">Resume/CV *</label><input id="resume" name="resume" type="file" accept=".pdf" required>
              <label for="linkedin">LinkedIn Profile</label><input id="linkedin" name="linkedin">
              <label for="q_years">How many years of professional experience do you have with C#? *</label><input id="q_years" name="q_years" required>
              <label for="q_auth">Are you legally authorized to work in Brazil? *</label>
              <select id="q_auth" name="q_auth" required><option value="">Select...</option><option>Yes</option><option>No</option></select>
              <label for="q_sponsor">Will you now or in the future require visa sponsorship? *</label>
              <select id="q_sponsor" name="q_sponsor" required><option value="">Select...</option><option>Yes</option><option>No</option></select>
              {extra}
              <button type="submit">Submit Application</button>
            </form>
            """);
    }
}
