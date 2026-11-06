using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Formulário do Workable (jobs.workable.com e apply.workable.com). O formulário abre no botão "Apply now", os rótulos envolvem os
/// campos (sem for) e as perguntas de sim ou não vêm em grupos com o texto ligado por aria-labelledby. Responde só o que sai do
/// perfil: anos com a tecnologia pelas datas, nível de inglês, região pelo local, idioma do currículo, o banco de respostas e as
/// respostas que você já deu. Declaração (li e concordo, certifico) e pergunta sem resposta param em você.
/// </summary>
internal sealed partial class WorkableForm(BrowserPool browsers, IHttpClientFactory http, GuardKey key, TimeProvider time, IOptions<AutomationOptions> options)
    : LabelForm(browsers, http, key, time, options)
{
    public override string Id => "workable-form";
    public override string Name => "Formulário Workable";
    protected override string[] RealDomains => ["jobs.workable.com", "apply.workable.com"];
    // Os arquivos da página do Workable (scripts, estilos, ícones). O challenges.cloudflare.com fica de fora de propósito.
    protected override string[] AssetHosts => ["dcvxs6ggqztsa.cloudfront.net", "workablehr.s3.amazonaws.com", "workablehr-ui.s3.amazonaws.com"];
    protected override Regex FakePath => Path();

    private sealed record Found(string Id, string Name, string Tag, string Type, bool Required, string Label, string Group, int Nth);

    /// <summary>Marca a opção do rádio pelo nome do grupo e pelo texto dela, e diz se ficou marcada.</summary>
    private const string PickRadio = """
        ([name, label]) => {
          const norm = (t) => (t || '').replace(/\s+/g, ' ').trim().toLowerCase();
          const r = [...document.querySelectorAll('input[type=radio]')].filter(x => x.name === name)
            .find(x => norm(x.closest('label')?.textContent) === norm(label) || norm(document.querySelector('label[for="' + CSS.escape(x.id) + '"]')?.textContent) === norm(label));
          if (!r) return false;
          // Clica no texto da opção, como uma pessoa: o rádio de verdade fica escondido e a página só registra o clique no rótulo.
          (r.closest('label') || document.querySelector('label[for="' + CSS.escape(r.id) + '"]') || r).click();
          return new Promise((done) => setTimeout(() => done(r.checked || r.closest('[role=radio]')?.getAttribute('aria-checked') === 'true'
            || [...document.querySelectorAll('input[type=radio]')].some(x => x.name === name && (x.checked || x.closest('[role=radio]')?.getAttribute('aria-checked') === 'true'))), 150));
        }
        """;

    /// <summary>Cada campo visível do formulário com o rótulo de verdade (for, rótulo em volta, aria-labelledby ou aria-label) e, no rádio, a pergunta do grupo.</summary>
    private const string Discover = """
        () => {
          const form = document.querySelector('form'); if (!form) return '[]';
          const text = (el) => (el?.textContent || '').replace(/SVGs not supported by this browser\./g, ' ').replace(/\s+/g, ' ').trim();
          const byIds = (ids) => (ids || '').split(/\s+/).map(i => text(document.getElementById(i))).join(' ').trim();
          const labelOf = (e) => {
            if (e.id) { const l = document.querySelector('label[for="' + CSS.escape(e.id) + '"]'); if (l && text(l)) return text(l); }
            const w = e.closest('label'); if (w && text(w)) return text(w);
            // Sem rótulo comum: o texto ligado por aria, o exemplo do campo ou, por último, o nome interno dele.
            return byIds(e.getAttribute('aria-labelledby')) || e.getAttribute('aria-label') || e.getAttribute('placeholder') || (e.name || '').replace(/[_-]+/g, ' ');
          };
          const groupOf = (e) => {
            const rg = e.closest('[role=radiogroup], [role=group]');
            const t = rg ? (byIds(rg.getAttribute('aria-labelledby')) || rg.getAttribute('aria-label') || '') : '';
            return t || text(e.closest('fieldset')?.querySelector('legend'));
          };
          const out = []; let files = 0;
          for (const e of form.querySelectorAll('input, select, textarea')) {
            const type = (e.type || '').toLowerCase();
            if (type === 'hidden' || type === 'submit' || e.disabled) continue;
            if (type !== 'file' && type !== 'radio' && type !== 'checkbox' && !(e.offsetWidth || e.offsetHeight || e.getClientRects().length)) continue;
            if (!e.id) e.id = 'jo-' + Math.random().toString(36).slice(2, 10);
            out.push({ id: e.id, name: e.name || '', tag: e.tagName.toLowerCase(), type, required: !!e.required || e.getAttribute('aria-required') === 'true',
              label: labelOf(e), group: type === 'radio' || type === 'checkbox' ? groupOf(e) : '', nth: type === 'file' ? files++ : 0 });
          }
          return JSON.stringify(out);
        }
        """;

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

        var found = JsonSerializer.Deserialize<Found[]>(await page.EvaluateAsync<string>(Discover), JsonSerializerOptions.Web) ?? [];
        var fields = new List<FieldValue>();
        var missing = new List<string>();
        // O campo do currículo: o primeiro com nome de currículo; sem nenhum, o primeiro que não é o da carta.
        var resumeAt = found.Where(f => f.Type == "file").FirstOrDefault(f => Resume().IsMatch(Clean(f.Label)))?.Nth
            ?? found.Where(f => f.Type == "file" && !Cover().IsMatch(Clean(f.Label))).Select(f => (int?)f.Nth).FirstOrDefault();
        // Rádios se agrupam pelo nome; caixas de marcar, pela pergunta (no Workable cada opção de uma lista tem nome próprio).
        foreach (var group in found.GroupBy(f => f.Type == "radio" && f.Name.Length > 0 ? $"radio:{f.Name}"
            : f.Type == "checkbox" && Clean(f.Group).Length > 0 ? $"checkbox:{Clean(f.Group)}" : f.Type == "checkbox" && f.Name.Length > 0 ? $"checkbox:{f.Name}" : f.Id))
        {
            var f = group.First();
            var label = Clean(f.Label);
            if (f.Type == "radio")
            {
                var question = Clean(f.Group);
                var answer = YesNo(question, m);
                var choice = answer is { } a ? group.FirstOrDefault(o => Picks(Clean(o.Label), a.Value)) : null;
                if (choice is null)
                {
                    if (group.Any(o => o.Required)) missing.Add(question.Length > 0 ? question : label);
                    continue;
                }
                // A opção do Workable é um bloco (role=radio) com o rádio de verdade escondido dentro: clica no bloco, como uma
                // pessoa, e confere a marca nele (aria-checked). Sem bloco, marca pelo nome do grupo e pelo texto da opção.
                if (!await PickAsync(page, f.Name, Clean(choice.Label)))
                    return AdapterResult<PreparedForm>.Fail(AdapterResultKind.RetryableFailure, "fill-mismatch", $"A resposta de \"{question}\" não ficou marcada.");
                fields.Add(new FieldValue(question, Clean(choice.Label), answer!.Value.Source));
                continue;
            }
            if (f.Type == "checkbox" && (group.Count() > 1 || Clean(f.Group) is { Length: > 0 } asked && !Declaration().IsMatch(asked) && !Declaration().IsMatch(label)))
            {
                // Lista para marcar (quais tecnologias você usa, por exemplo) é escolha sua, não declaração: o app não marca opção por
                // você. Obrigatória, a vaga fica com você; opcional, fica em branco.
                var question = Clean(f.Group.Length > 0 ? f.Group : label);
                if (group.Any(o => o.Required))
                    return AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "choice", $"O formulário pede para marcar opções em \"{(question.Length > 120 ? question[..120] + "…" : question)}\". Fica com você.");
                continue;
            }
            if (f.Type == "checkbox" || Declaration().IsMatch(label))
            {
                if (f.Required) return AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "declaration",
                    $"O formulário pede uma declaração: \"{(label.Length > 120 ? label[..120] + "…" : label)}\". Só você marca.");
                continue;
            }
            if (f.Type == "file")
            {
                if (f.Nth != resumeAt) { if (f.Required) missing.Add(label); continue; }
                await page.Locator("form input[type=file]").Nth(f.Nth).SetInputFilesAsync(m.ResumePdfPath);
                fields.Add(new FieldValue("Currículo", $"{System.IO.Path.GetFileName(m.ResumePdfPath)} (sha256 {FileHash(m.ResumePdfPath)[..12]})", "currículo"));
                continue;
            }
            var (value, source) = Resolve(label, f.Type, m);
            if (value is null)
            {
                if (f.Required) missing.Add(label);
                continue;
            }
            var control = page.Locator(f.Name.Length > 0 ? $"form [name='{f.Name.Replace("'", "\\'")}']" : Css(f.Id)).First;
            if (f.Tag == "select")
            {
                var options = await control.Locator("option").AllInnerTextsAsync();
                var match = options.FirstOrDefault(o => o.Trim().Equals(value, StringComparison.OrdinalIgnoreCase));
                if (match is null) { if (f.Required) missing.Add(label); continue; }
                await control.SelectOptionAsync(new SelectOptionValue { Label = match.Trim() });
                fields.Add(new FieldValue(label, match.Trim(), source));
                continue;
            }
            await control.FillAsync(value);
            var got = await control.InputValueAsync();
            // O telefone ganha máscara do país: vale se os dígitos batem.
            if (f.Type == "tel" ? Last8(got) != Last8(value) : got != value)
                return AdapterResult<PreparedForm>.Fail(AdapterResultKind.RetryableFailure, "fill-mismatch", $"O campo \"{label}\" não guardou o valor.");
            fields.Add(new FieldValue(label, value, source));
        }
        if (missing.Count > 0) return Missing(missing);
        if (fields.Count == 0) return AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "no-fields", "Nenhum campo com rótulo foi reconhecido.");
        return AdapterResult<PreparedForm>.Ok(new PreparedForm(Id, Version, new Uri(target.ApplyUrl).Authority, PayloadHash(fields), fields));
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

    private static async Task<bool> PickAsync(IPage page, string name, string label)
    {
        var option = page.Locator($"[role=radio]:has(input[type=radio][name='{name.Replace("'", "\\'")}'])")
            .Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(label)}\s*$", RegexOptions.IgnoreCase) }).First;
        if (await option.CountAsync() > 0)
        {
            try
            {
                await option.ClickAsync(new LocatorClickOptions { Timeout = 5000 });
                if (await option.GetAttributeAsync("aria-checked") == "true" || await option.Locator("input[type=radio]").IsCheckedAsync()) return true;
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { /* cai no clique direto abaixo */ }
        }
        return await page.EvaluateAsync<bool>(PickRadio, new[] { name, label });
    }

    /// <summary>Sim ou não que sai do perfil ou das suas respostas; sem base, fica nulo e a pergunta para em você.</summary>
    private static (string Value, string Source)? YesNo(string q, MaterialBundle m)
    {
        if (m.Answers.TryGetValue(CustomAnswer.Key(q), out var mine)) return (mine, "sua resposta");
        if (Years().Match(q) is { Success: true } y && int.TryParse(y.Groups["n"].Value, out var need))
        {
            var tech = y.Groups["tech"].Value.Trim().TrimEnd('?', '.', ' ');
            var key = m.Answers.Keys.FirstOrDefault(k => k.StartsWith("years_experience:", StringComparison.Ordinal)
                && (string.Equals(k[17..], tech, StringComparison.OrdinalIgnoreCase) || tech.StartsWith(k[17..] + " ", StringComparison.OrdinalIgnoreCase)
                    || k[17..].StartsWith(tech + " ", StringComparison.OrdinalIgnoreCase)));
            return key is not null && int.TryParse(m.Answers[key], out var have) ? (have >= need ? "yes" : "no", $"{have} anos de {key[17..]} pelas datas das experiências") : null;
        }
        if (English().IsMatch(q) && Rank(q) is { } wanted && m.Answers.TryGetValue("english_level", out var level) && Rank(level) is { } mineLevel)
            return (mineLevel >= wanted ? "yes" : "no", $"inglês {level} no perfil");
        if (CvInEnglish().IsMatch(q))
            return (System.IO.Path.GetFileName(m.ResumePdfPath).Contains("Resume", StringComparison.OrdinalIgnoreCase) ? "yes" : "no", "idioma do currículo anexado");
        if (Located().Match(q) is { Success: true } where && m.Answers.TryGetValue("location", out var home))
        {
            var region = where.Groups["where"].Value;
            var brazil = Brazil().IsMatch(home);
            if (LatAm().IsMatch(region)) return (brazil || LatAm().IsMatch(home) ? "yes" : "no", "local do perfil");
            if (Brazil().IsMatch(region)) return (brazil ? "yes" : "no", "local do perfil");
            return null;
        }
        foreach (var (pattern, answer) in Questions)
            if (pattern.IsMatch(q) && m.Answers.TryGetValue(answer, out var v)) return (v, "banco de respostas");
        return null;
    }

    /// <summary>A opção do rádio que corresponde à resposta: sim, yes e true valem igual; não, no e false também.</summary>
    private static bool Picks(string option, string answer)
    {
        var o = CustomAnswer.Normalize(option);
        var a = CustomAnswer.Normalize(answer);
        return o == a || Yes().IsMatch(o) && Yes().IsMatch(a) || No().IsMatch(o) && No().IsMatch(a);
    }

    /// <summary>Nível de inglês em degraus: básico 1, intermediário 2, intermediário avançado (B2) 3, avançado (C1) 4, fluente 5, nativo 6.</summary>
    private static int? Rank(string s)
    {
        var t = s.ToLowerInvariant();
        if (Regex.IsMatch(t, @"native|nativ")) return 6;
        if (Regex.IsMatch(t, @"fluent|fluente|\bc2\b")) return 5;
        if (Regex.IsMatch(t, @"upper[- ]?intermediate|intermedi[aá]rio[- ]avan[cç]ado|\bb2\b")) return 3;
        if (Regex.IsMatch(t, @"advanced|avan[cç]ado|\bc1\b")) return 4;
        if (Regex.IsMatch(t, @"intermediate|intermedi[aá]rio|\bb1\b")) return 2;
        if (Regex.IsMatch(t, @"basic|b[aá]sico|beginner|\ba[12]\b")) return 1;
        return null;
    }

    private static string Css(string id) => $"[id='{id.Replace("'", "\\'")}']";
    private static string Last8(string s) { var d = new string([.. s.Where(char.IsDigit)]); return d.Length > 8 ? d[^8..] : d; }

    [GeneratedRegex(@"^/__sem-formulario-falso__$")] private static partial Regex Path();
    [GeneratedRegex(@"no longer accepting|not accepting applications|this job is closed|vaga encerrada", RegexOptions.IgnoreCase)] private static partial Regex NoLonger();
    [GeneratedRegex(@"\bI (confirm|agree|accept|declare|certify|acknowledge)\b|\bli e (concordo|aceito)|\bdeclaro\b|\bconcordo com\b|\bcertifico\b", RegexOptions.IgnoreCase)] private static partial Regex Declaration();
    [GeneratedRegex(@"(?<n>\d+)\s*\+?\s*(?:or more\s+)?(?:years?|anos)\b.*?\b(?:with|in|of|de|com|em)\s+(?<tech>[\w#.+]+(?:\s[\w#.+]+)?)\s*\??$", RegexOptions.IgnoreCase)] private static partial Regex Years();
    [GeneratedRegex(@"english|ingl[eê]s", RegexOptions.IgnoreCase)] private static partial Regex English();
    [GeneratedRegex(@"cover letter|carta", RegexOptions.IgnoreCase)] private static partial Regex Cover();
    [GeneratedRegex(@"\b(cv|resume|curr[ií]culo)\b.*\b(in english|em ingl[eê]s)\b", RegexOptions.IgnoreCase)] private static partial Regex CvInEnglish();
    [GeneratedRegex(@"\b(located|based|living|reside|resident|mora|reside)\b.*?\bin\s+(?<where>[^?(]+)|\b(mora|reside) n[oa]\s+(?<where>[^?(]+)", RegexOptions.IgnoreCase)] private static partial Regex Located();
    [GeneratedRegex(@"latin america|latam|am[eé]rica latina|south america|am[eé]rica do sul", RegexOptions.IgnoreCase)] private static partial Regex LatAm();
    [GeneratedRegex(@"\bbra[sz]il\b", RegexOptions.IgnoreCase)] private static partial Regex Brazil();
    [GeneratedRegex(@"^(yes|sim|y|s|true|verdadeiro)$")] private static partial Regex Yes();
    [GeneratedRegex(@"^(no|nao|n|false|falso)$")] private static partial Regex No();
}
