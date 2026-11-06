using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// SmartRecruiters (jobs.smartrecruiters.com): o formulário fica no "I'm interested" (oneclick-ui) e, para navegador automático,
/// aparece atrás da verificação anti-robô da DataDome. O app não passa por ela: abre, confere e, com a verificação, a vaga fica com
/// você. Sem a verificação, segue o mesmo preenchimento por rótulo dos outros formulários.
/// </summary>
internal sealed partial class SmartRecruitersForm(BrowserPool browsers, IHttpClientFactory http, GuardKey key, TimeProvider time, IOptions<AutomationOptions> options)
    : LabelForm(browsers, http, key, time, options)
{
    public override string Id => "smartrecruiters-form";
    public override string Name => "Formulário SmartRecruiters";
    protected override string[] RealDomains => ["jobs.smartrecruiters.com", "www.smartr.me"];
    protected override Regex FakePath => Path();

    protected override async Task<AdapterResult<PreparedForm>> FillAsync(IPage page, ApplyTarget target, MaterialBundle m)
    {
        var url = target.ApplyUrl;
        if (!url.Contains("/oneclick-ui/", StringComparison.Ordinal))
        {
            var response = await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            if (response is null || response.Status is 404 or 410)
                return AdapterResult<PreparedForm>.Fail(AdapterResultKind.PermanentFailure, "posting-closed", "A vaga não está mais no ar.");
            await page.WaitForTimeoutAsync(3000);
            if (await Blocked(page)) return Wall();
            var link = page.Locator("a[href*='/oneclick-ui/']");
            // Sem o link e sem a verificação à vista: o SmartRecruiters não mostrou a candidatura para navegador automático.
            if (await link.CountAsync() == 0 || await link.First.GetAttributeAsync("href") is not { Length: > 0 } href) return Wall();
            url = href;
        }
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForTimeoutAsync(3000);
        if (await Blocked(page)) return Wall();
        return await base.FillAsync(page, target with { ApplyUrl = url }, m);
    }

    private static async Task<bool> Blocked(IPage page) => await page.Locator(BotCheck + ", iframe[src*='captcha-delivery.com']").CountAsync() > 0;

    private static AdapterResult<PreparedForm> Wall() => AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "captcha",
        "O SmartRecruiters mostra uma verificação anti-robô para navegador automático: o app não passa por ela, a candidatura fica com você.");

    [GeneratedRegex(@"^/__sem-formulario-falso__$")] private static partial Regex Path();
}
