using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobOrchestrator.Applications.Data;
using JobOrchestrator.Applications.Domain;
using JobOrchestrator.BuildingBlocks.Data;
using Microsoft.EntityFrameworkCore;

namespace JobOrchestrator.Tests.EndToEnd;
/// <summary>Real local API + PostgreSQL. No external sites, models, email or submissions.</summary>
[Trait("Category", "E2E")]
public sealed class FormQuestionsDbTests
{
    [Fact]
    public async Task One_answer_persists_and_resumes_only_matching_questions_once()
    {
        await using var stack = await Stack.StartAsync();
        await stack.KillAsync("worker");
        var options = new DbContextOptionsBuilder<ApplicationsDbContext>();
        options.UseModuleDatabase(stack.Connection, "applications");
        await using var db = new ApplicationsDbContext(options.Options);
        var now = DateTime.UtcNow;
        JobApplication Waiting(string question, HumanTaskKind kind = HumanTaskKind.Question)
        {
            var a = JobApplication.Create(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N"), "Synthetic employer", "Test role", "Local", "https://example.test/job", 80), now);
            a.MaterialsReady("/tmp/synthetic-resume.pdf", new string('a', 64), now);
            a.Move(ApplicationState.FormPreparing, "Test prepare", "Test", now);
            a.Move(ApplicationState.NeedsHuman, "Missing answer", "Test", now, new { questions = new[] { new FormQuestion(question, "text", Array.Empty<string>()) } });
            db.Applications.Add(a); db.AddTask(a.Id, kind, "Missing answer", "Synthetic fixture", now);
            return a;
        }
        var one = Waiting("Data de nascimento"); var two = Waiting("Date of birth");
        var other = Waiting("Qual seu salário atual?"); var login = Waiting("Data de nascimento", HumanTaskKind.Login); var uncertain = Waiting("Data de nascimento", HumanTaskKind.Reconcile);
        await db.SaveChangesAsync();
        var pending = await stack.GetAsync("/applications/questions");
        Assert.Equal(2, Assert.Single(pending.EnumerateArray(), q => q.GetProperty("kind").GetString() == "date").GetProperty("applications").GetArrayLength());
        // The form questions, and apart from them the send the app could not confirm, asked as "did it arrive?".
        Assert.Equal(2, pending.EnumerateArray().Count(q => q.GetProperty("kind").GetString() != "arrived"));
        Assert.Single(pending.EnumerateArray(), q => q.GetProperty("kind").GetString() == "arrived");
        Assert.Equal(HttpStatusCode.BadRequest, await stack.PutAsync("/applications/answers", new { question = "Data de nascimento", answer = "2099-02-29" }));
        using var result = await stack.Http.PostAsJsonAsync(stack.Api + "/applications/questions/answers", new { answers = new[] { new { question = "Data de nascimento", answer = "2000-02-29" } } });
        result.EnsureSuccessStatusCode();
        Assert.Equal(2, (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resumed").GetInt32());
        using var repeated = await stack.Http.PutAsJsonAsync(stack.Api + "/applications/answers", new { question = "DOB", answer = "2000-02-29" });
        repeated.EnsureSuccessStatusCode();
        Assert.Equal(0, (await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resumed").GetInt32());
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.WorkItems.CountAsync(i => i.Step == WorkStep.Prepare && i.CompletedAt == null));
        Assert.Equal(0, await db.WorkItems.CountAsync(i => i.Step == WorkStep.Submit));
        foreach (var a in new[] { one, two }) Assert.Equal(ApplicationState.FormPreparing, (await db.Applications.SingleAsync(x => x.Id == a.Id)).State);
        foreach (var a in new[] { other, login, uncertain }) Assert.Equal(ApplicationState.NeedsHuman, (await db.Applications.SingleAsync(x => x.Id == a.Id)).State);
        Assert.Single((await stack.GetAsync("/applications/questions")).EnumerateArray(), q => q.GetProperty("kind").GetString() != "arrived");
        var stored = await File.ReadAllTextAsync(Path.Combine(stack.DataDir, "answers.json"));
        Assert.Contains("2000-02-29", stored); Assert.DoesNotContain("2099", stored);
        Assert.Equal(0, await stack.SubmissionsAsync());
    }
}
