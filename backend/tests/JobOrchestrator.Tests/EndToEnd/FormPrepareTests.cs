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

/// <summary>The form fill, on actual Chromium and HTTP on loopback; synthetic identity, no third-party site, and nothing is ever sent.</summary>
[Trait("Category", "E2E")]
public sealed class FormPrepareTests
{
    [Theory]
    [InlineData(false, AdapterResultKind.Completed)]
    [InlineData(true, AdapterResultKind.Unsupported)]
    public async Task A_different_preselected_radio_is_not_proof_the_requested_answer_was_filled(bool blockChange, AdapterResultKind expected)
    {
        var radios = "<fieldset><legend>Do you need sponsorship?</legend><label><input type=radio name=sponsorship value=no checked required>No</label>"
            + $"<label {(blockChange ? "onclick=\"event.preventDefault()\"" : "")}><input type=radio name=sponsorship value=yes required>Yes</label></fieldset>";
        var html = Form("accepted").Replace("<button type=\"submit\">", radios + "<button type=\"submit\">", StringComparison.Ordinal);
        await using var site = await TestSite.StartAsync(app => app.MapGet("/job", () => Results.Content(html, "text/html")));
        var options = Options.Create(new AutomationOptions { FakeAtsHosts = [new Uri(site.Url).Authority], Browser = new() { TimeoutSeconds = 2 } });
        await using var browser = new BrowserPool(options, NullLogger<BrowserPool>.Instance);
        using var folder = new TestFiles();
        var adapter = new GenericForm(browser, options);
        var bundle = folder.Bundle with { Answers = new Dictionary<string, string> { ["needs_sponsorship"] = "Yes" } };
        var result = await adapter.PrepareAsync(new(Guid.NewGuid(), site.Url + "/job"), bundle, default);
        Assert.True(expected == result.Kind, $"Expected {expected}, got {result.Kind}: {result.ReasonCode}: {result.Detail}");
        if (expected == AdapterResultKind.Completed) Assert.Contains(result.Value!.Fields, f => f.Label == "Do you need sponsorship?" && f.Value == "Yes");
    }

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
        var adapter = new GenericForm(browser, options);
        var bundle = folder.Bundle with { Answers = new Dictionary<string, string> { [CustomAnswer.Key(question)] = answer } };
        var result = await adapter.PrepareAsync(new(Guid.NewGuid(), site.Url + "/job"), bundle, default);
        Assert.True(expected == result.Kind, $"Expected {expected}, got {result.Kind}: {result.ReasonCode}: {result.Detail}");
        if (expected == AdapterResultKind.Completed) Assert.Contains(result.Value!.Fields, f => f.Label == question && f.Value == "6000");
    }

    [Fact]
    public async Task Unanswered_required_questions_reach_the_answerer_contract()
    {
        await using var site = await TestSite.StartAsync(app => app.MapGet("/job", () => Results.Content(Form("questions"), "text/html")));
        var options = Options.Create(new AutomationOptions { FakeAtsHosts = [new Uri(site.Url).Authority], Browser = new() { TimeoutSeconds = 2 } });
        await using var browser = new BrowserPool(options, NullLogger<BrowserPool>.Instance);
        using var folder = new TestFiles();
        var adapter = new GreenhouseLikeForm(browser, options);
        var result = await adapter.PrepareAsync(new(Guid.NewGuid(), site.Url + "/job"), folder.Bundle, default);
        Assert.Equal(AdapterResultKind.Unsupported, result.Kind);
        var question = Assert.Single(result.Questions!);
        Assert.Equal("Describe a feature you owned", question.Label);
        Assert.Equal("textarea", question.Kind);
    }

    [Theory]
    [InlineData("date", "", "2000-02-29")]
    [InlineData("text", "DD/MM/AAAA", "29/02/2000")]
    [InlineData("text", "MM/DD/YYYY", "02/29/2000")]
    public async Task User_birth_date_fills_browser_controls_without_submitting(string type, string placeholder, string expected)
    {
        var html = Form("accepted").Replace("<button type=\"submit\">", $"<label for=bday>Data de nascimento</label><input id=bday type={type} placeholder='{placeholder}' required><button type=\"submit\">", StringComparison.Ordinal);
        await using var site = await TestSite.StartAsync(app => app.MapGet("/job", () => Results.Content(html, "text/html")));
        var options = Options.Create(new AutomationOptions { FakeAtsHosts = [new Uri(site.Url).Authority], Browser = new() { TimeoutSeconds = 2 } });
        await using var browser = new BrowserPool(options, NullLogger<BrowserPool>.Instance);
        using var folder = new TestFiles();
        var bundle = folder.Bundle with { Answers = new Dictionary<string, string> { [CustomAnswer.Key("Date of birth")] = "2000-02-29" } };
        foreach (IFormAdapter adapter in new IFormAdapter[] { new GenericForm(browser, options), new GreenhouseLikeForm(browser, options) })
        {
            var result = await adapter.PrepareAsync(new(Guid.NewGuid(), site.Url + "/job"), bundle, default);
            Assert.True(result.Kind == AdapterResultKind.Completed, $"{adapter.Id}: {result.ReasonCode}: {result.Detail}");
            Assert.Contains(result.Value!.Fields, f => f.Label == "Data de nascimento" && f.Value == expected);
        }
    }

    private static string Form(string mode) => $$"""
        <!doctype html><html><head><meta charset="utf-8"></head><body>
        <form method="post" action="/submit">
          <label for="first">First name</label><input id="first" name="first" required>
          <label for="last">Last name</label><input id="last" name="last" required>
          <label for="email">Email</label><input id="email" name="email" type="email" required>
          <label for="resume">Resume</label><input id="resume" name="resume" type="file" required>
          {{(mode == "questions" ? "<label for=question>Describe a feature you owned</label><textarea id=question required></textarea>" : "")}}
          <button type="submit">Submit application</button>
        </form></body></html>
        """;

    private sealed class TestFiles : IDisposable
    {
        private readonly string dir = Directory.CreateTempSubdirectory("jo-form-proof-").FullName;
        public MaterialBundle Bundle { get; }
        public TestFiles()
        {
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
