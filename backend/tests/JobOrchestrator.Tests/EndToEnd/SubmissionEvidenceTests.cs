using System.Net;
using JobOrchestrator.Automation.Adapters;
using JobOrchestrator.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobOrchestrator.Tests.EndToEnd;

/// <summary>Actual Chromium and HTTP on loopback; synthetic identity, no third-party application.</summary>
[Trait("Category", "E2E")]
public sealed class SubmissionEvidenceTests
{
    [Theory]
    [InlineData("Pretensão salarial mensal", "R$ 6.000 por mês (PJ)", AdapterResultKind.Completed)]
    [InlineData("Describe project 2026", "Delivered project 2026", AdapterResultKind.Unsupported)]
    public async Task Numeric_masks_preserve_salary_but_cannot_discard_words_from_other_answers(string question, string answer, AdapterResultKind expected)
    {
        var html = Form("accepted").Replace("<button type=\"submit\">", $"<label for=masked>{question}</label><input id=masked required oninput=\"this.value=this.value.replace(/\\D/g,'')\"><button type=\"submit\">", StringComparison.Ordinal);
        await using var site = await TestSite.StartAsync(app => app.MapGet("/job", () => Results.Content(html, "text/html")));
        var options = Options.Create(new AutomationOptions { FakeAtsHosts = [new Uri(site.Url).Authority], Browser = new() { TimeoutSeconds = 2 } });
        await using var browser = new BrowserPool(options, NullLogger<BrowserPool>.Instance);
        using var folder = new TestFiles();
        var adapter = new GenericForm(browser, new HttpFactory(), folder.Key, TimeProvider.System, options);
        var bundle = folder.Bundle with { Answers = new Dictionary<string, string> { [CustomAnswer.Key(question)] = answer } };
        var result = await adapter.PrepareAsync(new(Guid.NewGuid(), site.Url + "/job"), bundle, default);
        Assert.Equal(expected, result.Kind);
        if (expected == AdapterResultKind.Completed) Assert.Contains(result.Value!.Fields, f => f.Label == question && f.Value == "6000");
    }

    [Fact]
    public async Task Unanswered_required_questions_reach_the_answerer_contract()
    {
        await using var site = await TestSite.StartAsync(app => app.MapGet("/job", () => Results.Content(Form("questions"), "text/html")));
        var options = Options.Create(new AutomationOptions { FakeAtsHosts = [new Uri(site.Url).Authority], Browser = new() { TimeoutSeconds = 2 } });
        await using var browser = new BrowserPool(options, NullLogger<BrowserPool>.Instance);
        using var folder = new TestFiles();
        var adapter = new GreenhouseLikeForm(browser, new HttpFactory(), folder.Key, TimeProvider.System, options);
        var result = await adapter.PrepareAsync(new(Guid.NewGuid(), site.Url + "/job"), folder.Bundle, default);
        Assert.Equal(AdapterResultKind.Unsupported, result.Kind);
        var question = Assert.Single(result.Questions!);
        Assert.Equal("Describe a feature you owned", question.Label);
        Assert.Equal("textarea", question.Kind);
    }

    [Theory]
    [InlineData("stale", AdapterResultKind.SubmissionUnknown, 0)]
    [InlineData("hidden", AdapterResultKind.SubmissionUnknown, 0)]
    [InlineData("accepted", AdapterResultKind.Completed, 1)]
    [InlineData("rejected", AdapterResultKind.PermanentFailure, 0)]
    [InlineData("generic", AdapterResultKind.Completed, 1)]
    public async Task Only_a_new_visible_confirmation_proves_the_submission(string mode, AdapterResultKind expected, int accepted)
    {
        var received = 0;
        await using var site = await TestSite.StartAsync(app =>
        {
            app.MapGet("/job", () => Results.Content(Form(mode), "text/html"));
            app.MapPost("/submit", () =>
            {
                Interlocked.Increment(ref received);
                return Results.Content("<h1>Application received</h1><p>Application ID: TEST-1234</p>", "text/html");
            });
        });
        var options = Options.Create(new AutomationOptions { FakeAtsHosts = [new Uri(site.Url).Authority], Browser = new() { TimeoutSeconds = 2 } });
        await using var browser = new BrowserPool(options, NullLogger<BrowserPool>.Instance);
        using var folder = new TestFiles();
        IFormAdapter adapter = mode == "generic"
            ? new GenericForm(browser, new HttpFactory(), folder.Key, TimeProvider.System, options)
            : new GreenhouseLikeForm(browser, new HttpFactory(), folder.Key, TimeProvider.System, options);
        var target = new ApplyTarget(Guid.NewGuid(), site.Url + "/job");
        var prepared = await adapter.PrepareAsync(target, folder.Bundle, default);
        Assert.Equal(AdapterResultKind.Completed, prepared.Kind);
        Assert.Equal(0, received);
        var id = Guid.NewGuid(); var expires = DateTime.UtcNow.AddMinutes(1); var hash = prepared.Value!.PayloadHash;
        var token = new SubmitToken(id, target.ApplicationId, hash, expires, SubmitToken.Sign(folder.Key.Bytes, id, target.ApplicationId, hash, expires));
        var result = await adapter.SubmitAsync(target, folder.Bundle, token, default);
        Assert.Equal(expected, result.Kind);
        Assert.Equal(accepted, received);
        if (expected == AdapterResultKind.Completed)
        {
            Assert.Equal("TEST-1234", result.Value!.ReceiptId);
            Assert.Equal(4, result.Value.EvidenceLevel);
        }
    }

    private static string Form(string mode) => $$"""
        <!doctype html><html><body>
        {{(mode == "stale" ? "<p>Thank you for applying. Application received.</p>" : "")}}
        {{(mode == "hidden" ? "<p hidden>Application received</p>" : "")}}
        {{(mode == "generic" ? "<button onclick=\"document.body.innerHTML='Unrelated newsletter';\">Send</button>" : "")}}
        <form method="post" action="/submit" {{(mode is "stale" or "hidden" ? "onsubmit=\"event.preventDefault()\"" : "")}}
          {{(mode == "rejected" ? "onsubmit=\"event.preventDefault(); document.getElementById('error').innerHTML='<p role=alert>Invalid application</p>'\"" : "")}}>
          <label for="first">First name</label><input id="first" name="first" required>
          <label for="last">Last name</label><input id="last" name="last" required>
          <label for="email">Email</label><input id="email" name="email" type="email" required>
          <label for="resume">Resume</label><input id="resume" name="resume" type="file" required>
          {{(mode == "questions" ? "<label for=question>Describe a feature you owned</label><textarea id=question required></textarea>" : "")}}
          <button type="submit">Submit application</button>
        </form><div id="error"></div></body></html>
        """;

    private sealed class HttpFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
    private sealed class TestFiles : IDisposable
    {
        private readonly string dir = Directory.CreateTempSubdirectory("jo-form-proof-").FullName;
        public GuardKey Key { get; }
        public MaterialBundle Bundle { get; }
        public TestFiles()
        {
            Key = new(Path.Combine(dir, "guard.key"));
            var pdf = Path.Combine(dir, "resume.pdf"); File.WriteAllText(pdf, "%PDF-1.4 synthetic test file");
            Bundle = new("Ana", "Lima", "candidate@example.test", "123456789", "", pdf, new Dictionary<string, string>());
        }
        public void Dispose() => Directory.Delete(dir, true);
    }
}

internal sealed class TestSite(WebApplication app) : IAsyncDisposable
{
    public string Url => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    public static async Task<TestSite> StartAsync(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build(); map(app); await app.StartAsync(); return new(app);
    }
    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}
