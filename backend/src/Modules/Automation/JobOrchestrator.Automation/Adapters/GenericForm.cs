using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Formulário aberto do site da vaga, para os sites sem adapter próprio (Manatal, Quickin, Zoho Recruit, Talentlyft, Breezy, Recruitee,
/// BambooHR...). Abre a página, recusa os cookies opcionais, entra pelo botão de candidatura quando o formulário não está à vista,
/// escolhe o formulário que pede e-mail e currículo e preenche como os outros adapters. Login, CAPTCHA, bloqueio de robô, formulário
/// em etapas ou sem campo de currículo devolvem a vaga para você, com o currículo pronto. Sites que pedem conta (LinkedIn, Gupy,
/// Indeed, Catho, Workday) e agregadores que só repassam a vaga ficam de fora; as plataformas com adapter próprio também.
/// </summary>
internal sealed partial class GenericForm(BrowserPool browsers, IHttpClientFactory http, GuardKey key, TimeProvider time, IOptions<AutomationOptions> options)
    : LabelForm(browsers, http, key, time, options)
{
    /// <summary>The chosen form is marked on the page, so the fill and the send act on that one form only.</summary>
    private const string Scope = "[data-jo-form]";

    public override string Id => "generic-form";
    public override string Name => "Formulário aberto do site";
    protected override string[] RealDomains => [];
    protected override Regex FakePath => Fake();

    /// <summary>Sites with an account, aggregators that only pass the job on, and the platforms that have their own adapter.</summary>
    private static readonly string[] Excluded =
    [
        "linkedin.com", "indeed.com", "glassdoor.com", "glassdoor.com.br", "gupy.io", "catho.com.br", "infojobs.com.br", "vagas.com.br", "computrabajo.com",
        "pandape.com.br", "solides.com.br", "empregos.com.br", "trabalhabrasil.com.br", "getonbrd.com", "himalayas.app", "whatjobs.com", "wellfound.com", "angel.co",
        "arc.dev", "djinni.co", "geekhunter.com", "geekhunter.com.br", "powertofly.com", "remoteok.com", "remoteok.io", "weworkremotely.com", "remotive.com",
        "remotive.io", "jooble.org", "adzuna.com", "adzuna.com.br", "jobicy.com", "workingnomads.com", "themuse.com", "arbeitnow.com", "4dayweek.io",
        "github.com", "ycombinator.com", "upwork.com", "freelancer.com", "workana.com", "99freelas.com.br", "programathor.com.br", "trampos.co",
        "myworkdayjobs.com", "workday.com", "taleo.net", "icims.com", "oraclecloud.com", "successfactors.com", "successfactors.eu",
        "greenhouse.io", "lever.co", "ashbyhq.com", "workable.com", "smartrecruiters.com", "smartr.me",
    ];

    /// <summary>Second-level public suffixes: in "empresa.com.br" the site is "empresa.com.br", not "com.br".</summary>
    private static readonly HashSet<string> SecondLevel = new(StringComparer.OrdinalIgnoreCase)
    {
        "com.br", "net.br", "org.br", "gov.br", "co.uk", "org.uk", "com.mx", "com.ar", "com.co", "com.pe", "com.uy", "com.cl", "com.au", "co.nz", "co.za", "co.in",
        "com.pt", "co.jp", "com.tr", "com.sg", "com.es",
    };

    public override double Fit(Uri url)
    {
        if (Options.FakeAtsHosts.Contains(url.Authority, StringComparer.OrdinalIgnoreCase)) return FakePath.IsMatch(url.AbsolutePath) ? 1 : 0;
        if (url.Scheme is not ("http" or "https") || url.IsLoopback || System.Net.IPAddress.TryParse(url.Host, out _)) return 0;
        return Excluded.Any(d => url.Host.Equals(d, StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)) ? 0 : 0.5;
    }

    /// <summary>The site's own name: "jobs.quickin.io" is quickin.io, so its API and files load and its other subdomains open.</summary>
    internal static string Registrable(string host)
    {
        var parts = host.Split('.');
        if (parts.Length <= 2) return host;
        var two = string.Join('.', parts[^2..]);
        return SecondLevel.Contains(two) ? string.Join('.', parts[^3..]) : two;
    }

    // Any public host the page needs (its API often lives on another domain); pages only open inside the site.
    protected override Task<BrowserLease> LeaseAsync(ApplyTarget target, CancellationToken ct)
    {
        var url = new Uri(target.ApplyUrl);
        return Options.FakeAtsHosts.Contains(url.Authority, StringComparer.OrdinalIgnoreCase)
            ? Browsers.LeaseAsync([url.Authority], ct)
            : Browsers.LeaseAsync(["*"], ct, navigationDomain: Registrable(url.Host));
    }

    protected override ILocator SubmitControl(IPage page) =>
        page.Locator($"{Scope} button[type=submit]:visible, {Scope} input[type=submit]:visible")
            .Or(page.Locator($"{Scope} button:visible, {Scope} [role=button]:visible").Filter(new LocatorFilterOptions { HasTextRegex = Send() })).First;

    protected override Regex Confirmed => Thanks();

    protected override async Task<AdapterResult<PreparedForm>> FillAsync(IPage page, ApplyTarget target, MaterialBundle m)
    {
        var response = await page.GotoAsync(target.ApplyUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (response is null || response.Status is 404 or 410)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.PermanentFailure, "posting-closed", "A vaga não está mais no ar.");
        if (response.Status is 401 or 403 or 429 or >= 500)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "blocked", $"O site recusou o navegador automático (HTTP {response.Status}).");
        await SettleAsync(page);
        await DeclineCookiesAsync(page);
        if (!await PickFormAsync(page))
        {
            // The form often opens behind an "Apply" button, on the same page or on the next one.
            var apply = page.Locator("a:visible, button:visible, [role=button]:visible").Filter(new LocatorFilterOptions { HasTextRegex = Apply() }).First;
            if (await apply.CountAsync() > 0)
            {
                try { await apply.ClickAsync(new LocatorClickOptions { Timeout = 8000 }); }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { await apply.EvaluateAsync("e => e.click()"); }
                await SettleAsync(page);
                // A page that changes without loading again (single-page apps) draws the form a moment after the network rests.
                try { await page.Locator("input[type=email], input[type=file], input[autocomplete=email]").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 }); }
                catch (TimeoutException) { /* no form came: the checks below say why */ }
                await DeclineCookiesAsync(page);
            }
        }
        if (await page.Locator(BotCheck).CountAsync() > 0)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "captcha", "A página tem CAPTCHA ou verificação anti-robô: o app não passa por ela.");
        if (await page.Locator("input[type=password]:visible").CountAsync() > 0 || await page.GetByText(Mfa()).CountAsync() > 0)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "login", "O site pede login ou código de verificação.");
        if (!await PickFormAsync(page))
            return await page.GetByText(Closed()).CountAsync() > 0
                ? AdapterResult<PreparedForm>.Fail(AdapterResultKind.PermanentFailure, "posting-closed", "A vaga não aceita mais candidaturas.")
                : AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "no-form", "Não há formulário de candidatura aberto na página.");
        // A form in steps ("Next" without a sending button) goes past what the app can read in one look.
        if (await page.Locator($"{Scope} button:visible, {Scope} [role=button]:visible").Filter(new LocatorFilterOptions { HasTextRegex = Next() }).CountAsync() > 0
            && await page.Locator($"{Scope} button[type=submit]:visible, {Scope} input[type=submit]:visible").Or(page.Locator($"{Scope} button:visible").Filter(new LocatorFilterOptions { HasTextRegex = Send() })).CountAsync() == 0)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "multi-step", "O formulário é em etapas.");
        var filled = await FillFoundAsync(page, target, m, await DiscoverAsync(page, Scope, near: true), Scope);
        // A field drawn by the site's own widget (a custom list) that did not keep the value will not keep it on the next try either.
        if (filled.ReasonCode == "fill-mismatch")
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "custom-field", filled.Detail);
        // Without your e-mail in it, what got filled is not an application the company can answer: it goes back to you.
        if (filled.Kind == AdapterResultKind.Completed && !filled.Value!.Fields.Any(f => f.Value == m.Email))
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "no-email-field", "O app não reconheceu o campo de e-mail do formulário.");
        return filled;
    }

    /// <summary>
    /// Marks the application form: a form tag holding both an e-mail field and a file field (the one with more fields wins); without
    /// one, the smallest box holding the first of each. A resume-only upload box, a search box or a newsletter never wins.
    /// </summary>
    private static async Task<bool> PickFormAsync(IPage page) => await page.EvaluateAsync<bool>("""
        () => {
          const said = (e) => [e.name, e.id, e.placeholder, e.getAttribute('aria-label'), e.id && document.querySelector('label[for="' + CSS.escape(e.id) + '"]')?.textContent,
            e.closest('label')?.textContent].filter(Boolean).join(' ');
          // Only a visible e-mail field counts: a form hidden until "Apply" is opened first, not filled blind.
          const visible = (e) => !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
          const isMail = (e) => visible(e) && (e.type === 'email' || e.autocomplete === 'email' || (e.type === 'text' || !e.type) && /e-?mail/i.test(said(e)));
          const mailIn = (f) => [...f.querySelectorAll('input')].some(isMail);
          const fileIn = (f) => !!f.querySelector('input[type=file]');
          let best = [...document.querySelectorAll('form')].filter(f => mailIn(f) && fileIn(f))
            .sort((a, b) => b.querySelectorAll('input, select, textarea').length - a.querySelectorAll('input, select, textarea').length)[0];
          if (!best) {
            const mail = [...document.querySelectorAll('input')].find(isMail);
            const upload = document.querySelector('input[type=file]');
            if (mail && upload) { let n = mail; while (n && !n.contains(upload)) n = n.parentElement; best = n; }
          }
          document.querySelectorAll('[data-jo-form]').forEach(e => e.removeAttribute('data-jo-form'));
          if (!best || best === document.body || best === document.documentElement) return false;
          best.setAttribute('data-jo-form', '1');
          return true;
        }
        """);

    private static async Task SettleAsync(IPage page)
    {
        try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 8000 }); }
        catch (TimeoutException) { /* a page that keeps polling still gets read */ }
    }

    /// <summary>The cookie banner gets "reject" or "only necessary": declining optional cookies authorizes nothing in your name.</summary>
    private static async Task DeclineCookiesAsync(IPage page)
    {
        var decline = page.Locator("button:visible, a:visible, [role=button]:visible").Filter(new LocatorFilterOptions { HasTextRegex = Decline() }).First;
        if (await decline.CountAsync() == 0) return;
        try { await decline.ClickAsync(new LocatorClickOptions { Timeout = 4000 }); await page.WaitForTimeoutAsync(300); }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { /* the banner may close by itself or not cover the form */ }
    }

    [GeneratedRegex(@"^/generic/")] private static partial Regex Fake();
    [GeneratedRegex(@"^\s*(apply( now| here| for (this )?(job|position|role))?|i'?m interested|(candidatar(-se)?|candidate-se)( (à|a) vaga)?( agora)?|quero me candidatar|inscrever-se|inscreva-se|aplicar( (à|a) vaga)?( agora)?|postular(me|se)?( ahora)?|enviar candidatura)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Apply();
    [GeneratedRegex(@"^\s*(submit( (application|my application|your application))?|send( application)?|apply|apply now|enviar( candidatura| inscri[cç][aã]o)?|candidatar(-se)?|finalizar( candidatura| inscri[cç][aã]o)?|concluir( candidatura)?|aplicar|postular(me)?|enviar postulaci[oó]n)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Send();
    [GeneratedRegex(@"^\s*(next|pr[oó]ximo|pr[oó]xima etapa|avan[cç]ar|continuar|continue|seguinte|siguiente)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Next();
    [GeneratedRegex(@"^\s*(reject|decline|deny|refuse)( all| non-essential| optional)?( cookies)?\s*$|^\s*(only|use only) (necessary|required|essential)( cookies)?\s*$|^\s*(rejeitar|recusar)( todos| tudo| cookies)?\s*$|^\s*(somente|apenas) (os )?(necess[aá]rios|essenciais)\s*$|^\s*rechazar( todo| todas| cookies)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Decline();
    [GeneratedRegex(@"thank(s| you)( so much)? for (applying|your (application|interest))|application (has been |was )?(received|submitted|sent|completed?)|we('ve| have) received your application|successfully (applied|submitted)|obrigad[oa] (por (se candidatar|sua candidatura|seu interesse|participar))|candidatura (foi )?(recebida|enviada|realizada|conclu[ií]da)|recebemos (a |sua )?candidatura|inscri[cç][aã]o (foi )?(realizada|conclu[ií]da|enviada)|gracias por (postularte|tu postulaci[oó]n|aplicar)|postulaci[oó]n (enviada|recibida)", RegexOptions.IgnoreCase)]
    private static partial Regex Thanks();
    [GeneratedRegex(@"no longer accepting|not accepting applications|this (job|position) (is|has been) (closed|filled)|vaga (encerrada|fechada|expirada)|processo seletivo encerrado|inscri[cç][oõ]es encerradas", RegexOptions.IgnoreCase)]
    private static partial Regex Closed();
}
