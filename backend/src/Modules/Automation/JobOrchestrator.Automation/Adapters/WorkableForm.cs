using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Formulário do Workable (jobs.workable.com e apply.workable.com). O formulário abre no botão "Apply now", os rótulos envolvem os
/// campos (sem for) e as perguntas de sim ou não vêm em grupos com o texto ligado por aria-labelledby. Responde só o que sai do
/// perfil: anos com a tecnologia pelas datas, nível de inglês, região pelo local, idioma do currículo, o banco de respostas, as
/// respostas que você já deu e as que a IA escreveu pelos fatos do perfil. A declaração de privacidade e termos só é marcada com a
/// sua autorização; pergunta que ninguém respondeu volta inteira (tipo e opções) para a IA.
/// </summary>
internal sealed partial class WorkableForm(BrowserPool browsers, IOptions<AutomationOptions> options)
    : LabelForm(browsers, options)
{
    public override string Id => "workable-form";
    public override string Name => "Formulário Workable";
    protected override string[] RealDomains => ["jobs.workable.com", "apply.workable.com"];
    // Os arquivos da página do Workable (scripts, estilos, ícones). O challenges.cloudflare.com fica de fora de propósito.
    protected override string[] AssetHosts => ["dcvxs6ggqztsa.cloudfront.net", "workablehr.s3.amazonaws.com", "workablehr-ui.s3.amazonaws.com"];
    protected override Regex FakePath => Path();

    protected override async Task<AdapterResult<PreparedForm>> FillAsync(IPage page, ApplyTarget target, MaterialBundle m)
    {
        var response = await page.GotoAsync(target.ApplyUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (response is null || response.Status is 404 or 410)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.PermanentFailure, "posting-closed", "A vaga não está mais no ar.");
        var first = page.Locator("form input[name=firstname]");
        await DeclineCookiesAsync(page);
        if (await first.CountAsync() == 0)
        {
            // No quadro do Workable, o formulário só aparece depois do "Apply now". O botão vem no HTML antes de a página ligar o clique:
            // espera a página assentar e tenta de novo se o primeiro clique caiu cedo demais.
            try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 15000 }); }
            catch (TimeoutException) { /* a página segue carregando anúncio ou métrica: o clique tenta mesmo assim */ }
            // A página tem dois "Apply now": o do topo e o que só aparece ao rolar. Clica no que está visível.
            var apply = page.Locator("[data-ui=overview-apply-now]:visible, [data-ui=apply-button]:visible, [data-ui=sticky-apply-now]:visible").First;
            for (var attempt = 0; attempt < 3 && await first.CountAsync() == 0; attempt++)
            {
                try
                {
                    // O aviso de cookies do Workable cobre o botão e o clique do mouse nunca chega. O botão é acionado direto, sem
                    // aceitar cookies por você (aceitar seria um consentimento seu).
                    await apply.EvaluateAsync("e => e.click()");
                    await first.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { await page.WaitForTimeoutAsync(1500); }
            }
            await DeclineCookiesAsync(page);
            if (await first.CountAsync() == 0)
                return await page.GetByText(NoLonger()).CountAsync() > 0
                    ? AdapterResult<PreparedForm>.Fail(AdapterResultKind.PermanentFailure, "posting-closed", "A vaga não aceita mais candidaturas.")
                    : AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "no-form",
                        $"O formulário do Workable não abriu ({await page.Locator("[data-ui$=apply-now]").CountAsync()} botões Apply now na página, {await page.Locator("[data-ui$=apply-now]:visible").CountAsync()} visíveis).");
        }
        if (await page.Locator(BotCheck).CountAsync() > 0)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "captcha", "A página tem verificação anti-robô (Cloudflare ou CAPTCHA): o app não passa por ela, o envio fica com você.");
        if (await page.Locator("input[type=password]").CountAsync() > 0 || await page.GetByText(Mfa()).CountAsync() > 0)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "login", "A página pede login ou código de verificação. Fica para a sessão assistida.");

        return await FillFoundAsync(page, target, m, await DiscoverAsync(page, "form", near: false), "form");
    }

    /// <summary>
    /// O aviso de cookies do Workable abre como janela sobre a página e engole os cliques. O app clica em "Decline all": recusar
    /// os cookies opcionais não autoriza nada em seu nome (aceitar, sim, seria um consentimento seu, e isso o app nunca faz).
    /// </summary>
    private static async Task DeclineCookiesAsync(IPage page)
    {
        var decline = page.Locator("[data-ui=cookie-consent-decline]:visible");
        if (await decline.CountAsync() == 0) return;
        try { await decline.First.ClickAsync(new LocatorClickOptions { Timeout = 5000 }); await page.Locator("[data-ui=backdrop]").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 }); }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { /* sem a janela de cookies fechar, o clique direto ainda tenta */ }
    }


    [GeneratedRegex(@"^/__sem-formulario-falso__$")] private static partial Regex Path();
    [GeneratedRegex(@"no longer accepting|not accepting applications|this job is closed|vaga encerrada", RegexOptions.IgnoreCase)] private static partial Regex NoLonger();
}
