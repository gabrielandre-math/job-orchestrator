using System.Net.Http.Json;
using JobOrchestrator.Automation.Adapters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;

namespace JobOrchestrator.Tests.EndToEnd;

internal sealed class BuiltWebTheoryAttribute : TheoryAttribute
{
    public BuiltWebTheoryAttribute()
    {
        if (!Directory.Exists(Environment.GetEnvironmentVariable("JO_WEB_TEST_ROOT"))) Skip = "Built web artifact required (provided by CI).";
    }
}

/// <summary>Production Angular build in Chromium, using controlled HTTP fixtures. This proves UI behavior, not a third-party ATS.</summary>
[Trait("Category", "E2E")]
public sealed class FormOnboardingUiTests
{
    [BuiltWebTheory]
    [InlineData(1280, 900)]
    [InlineData(390, 844)]
    public async Task A_new_question_opens_a_local_answer_modal_and_can_be_deferred_and_saved(int width, int height)
    {
        var root = Environment.GetEnvironmentVariable("JO_WEB_TEST_ROOT")!;
        await using var site = await TestSite.StartAsync(app =>
        {
            app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(root) });
            app.MapFallback(() => Results.File(Path.Combine(root, "index.html"), "text/html"));
        });
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ExecutablePath = new BrowserOptions().ExecutablePath });
        await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = height } });
        var page = await context.NewPageAsync();
        var saved = false;
        string? body = null;
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.RouteAsync("**/api/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            object data = path switch
            {
                "/api/terms" => new { accepted = true },
                "/api/onboarding" => new { done = true },
                "/api/applications/questions" => saved ? Array.Empty<object>() : new object[] { new { key = "custom:birth_date", label = "Data de nascimento", kind = "date", options = Array.Empty<string>(), applications = new[] { "test-one", "test-two" } } },
                "/api/applications/questions/answers" => new { resumed = 2 },
                "/api/overview" => new { campaign = (object?)null, jobs = new { total = 0 }, applications = new { awaitingApproval = 0 }, pendingTasks = saved ? 0 : 2, activity = Array.Empty<object>(), adapters = Array.Empty<object>() },
                _ => Array.Empty<object>()
            };
            if (path.EndsWith("/questions/answers", StringComparison.Ordinal)) { body = route.Request.PostData; saved = true; }
            await route.FulfillAsync(new() { Json = data });
        });
        await page.GotoAsync(site.Url + "/candidaturas");
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Salvar e continuar" })).ToBeDisabledAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
        if (width < 650) await page.GetByRole(AriaRole.Button, new() { Name = "Abrir menu", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Completar respostas" }).ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByLabel("Data de nascimento", new() { Exact = true }).FillAsync("2000-02-29");
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Salvar e continuar" })).ToBeEnabledAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        var screenshots = Environment.GetEnvironmentVariable("JO_UI_SCREENSHOTS");
        if (screenshots is not null) { Directory.CreateDirectory(screenshots); await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"form-question-{width}.png"), FullPage = true }); }
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Salvar e continuar" }).ClickAsync();
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
        Assert.True(saved); Assert.Contains("2000-02-29", body);
        Assert.Empty(errors);
    }
}
