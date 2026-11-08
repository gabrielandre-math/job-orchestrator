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
                "/api/applications/questions" => saved ? Array.Empty<object>() : new object[]
                {
                    new { key = "birth_date", kind = "date", title = "Data de nascimento", detail = "Pedida por Empresa Um e Empresa Dois.", options = Array.Empty<string>(), companies = new[] { "Empresa Um", "Empresa Dois" },
                        applications = new[] { "test-one", "test-two" }, items = new[] { new { key = "custom:birth_date", label = "Data de nascimento", kind = "text", options = Array.Empty<string>(), applications = new[] { "test-one", "test-two" } } }, draft = (string?)null, note = (string?)null },
                    new { key = "consent", kind = "consent", title = "2 vagas pedem o seu aceite dos termos e da política de privacidade", detail = "Sem o aceite, o formulário não sai.", options = new[] { "Sim", "Não" }, companies = new[] { "Empresa Um", "Empresa Dois" },
                        applications = new[] { "test-one", "test-two" }, items = new[] { new { key = "custom:li e aceito", label = "Li e aceito a Política de Privacidade", kind = "declaration", options = Array.Empty<string>(), applications = new[] { "test-one", "test-two" } } }, draft = (string?)null, note = (string?)null },
                },
                "/api/applications/questions/answers" => new { resumed = 2, handedOff = 0 },
                "/api/overview" => new { campaign = (object?)null, jobs = new { total = 0 }, applications = new { awaitingApproval = 0 }, pendingTasks = saved ? 0 : 2, activity = Array.Empty<object>(), adapters = Array.Empty<object>() },
                _ => Array.Empty<object>()
            };
            if (path.EndsWith("/questions/answers", StringComparison.Ordinal)) { body = route.Request.PostData; saved = true; }
            await route.FulfillAsync(new() { Json = data });
        });
        await page.GotoAsync(site.Url + "/candidaturas");
        // The questions never open over the page by themselves: a reminder in the corner opens them, and the page stays usable.
        var dialog = page.GetByRole(AriaRole.Dialog);
        var reminder = page.GetByRole(AriaRole.Button, new() { Name = "2 respostas suas destravam 2 candidaturas Responder" });
        await Assertions.Expect(reminder).ToBeVisibleAsync();
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
        await reminder.ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Salvar e continuar" })).ToBeDisabledAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
        // Deferred, the questions stay one click away on Início too: they are its next step, with the button that opens them.
        if (width < 650) await page.GetByRole(AriaRole.Button, new() { Name = "Abrir menu", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Início" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Responder agora" }).ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByLabel("Data de nascimento", new() { Exact = true }).FillAsync("2000-02-29");
        await dialog.GetByRole(AriaRole.Radio, new() { Name = "Aceito em meu nome" }).ClickAsync();
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Salvar e continuar" })).ToBeEnabledAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        var screenshots = Environment.GetEnvironmentVariable("JO_UI_SCREENSHOTS");
        if (screenshots is not null) { Directory.CreateDirectory(screenshots); await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"form-question-{width}.png"), FullPage = true }); }
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Salvar e continuar" }).ClickAsync();
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
        Assert.True(saved); Assert.Contains("2000-02-29", body); Assert.Contains("\"key\":\"consent\"", body); Assert.Contains("\"answer\":\"Sim\"", body);
        Assert.Empty(errors);
    }
}
