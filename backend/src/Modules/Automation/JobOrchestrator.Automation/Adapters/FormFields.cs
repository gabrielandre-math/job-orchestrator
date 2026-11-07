using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// The fill every label form shares once its page shows the form: each visible field read by the label a person sees, radios and box
/// lists grouped by their question, the resume in the file field, and values from the profile, the answer bank, your answers and
/// the ones the AI wrote from your profile. Questions nobody answered come back whole for the AI.
/// </summary>
internal abstract partial class LabelForm
{
    protected sealed record Found(string Id, string Name, string Tag, string Type, bool Required, string Label, string Group, int Nth);

    /// <summary>Marca a opção do rádio pelo nome do grupo e pelo texto dela, e diz se ficou marcada.</summary>
    private const string PickRadio = """
        ([name, label]) => {
          const norm = (t) => (t || '').replace(/SVGs not supported by this browser\./g, ' ').replace(/\s+/g, ' ').trim().toLowerCase();
          // The option's text may sit in its own label, in a label pointed at by aria-labelledby, or around the radio.
          const texts = (x) => {
            const w = x.closest('[role=radio]');
            const ids = ((w && w.getAttribute('aria-labelledby')) || x.getAttribute('aria-labelledby') || '').split(/\s+/).filter(Boolean);
            return [x.closest('label')?.textContent, document.querySelector('label[for="' + CSS.escape(x.id) + '"]')?.textContent, w?.textContent,
              ...ids.map((i) => document.getElementById(i)?.textContent)];
          };
          const r = [...document.querySelectorAll('input[type=radio]')].filter(x => x.name === name).find(x => texts(x).some(t => norm(t) === norm(label)));
          if (!r) return false;
          // Clica no texto da opção, como uma pessoa: o rádio de verdade fica escondido e a página só registra o clique no rótulo.
          (r.closest('label') || document.querySelector('label[for="' + CSS.escape(r.id) + '"]') || r.closest('[role=radio]') || r).click();
          // Read the current DOM after React redraws it. Another checked option is not proof this answer stayed selected.
          return new Promise((done) => setTimeout(() => done([...document.querySelectorAll('input[type=radio]')].some(x => x.name === name
            && texts(x).some(t => norm(t) === norm(label)) && (x.checked || x.closest('[role=radio]')?.getAttribute('aria-checked') === 'true'))), 150));
        }
        """;

    /// <summary>Ticks one box by its id, clicking its text as a person would (the real box is often hidden), and says if it stayed ticked.</summary>
    private const string TickBox = """
        (id) => {
          const e = document.getElementById(id); if (!e) return false; if (e.checked) return true;
          (e.closest('label') || document.querySelector('label[for="' + CSS.escape(id) + '"]') || e.closest('[role=checkbox]') || e).click();
          return new Promise((done) => setTimeout(() => done(e.checked || e.closest('[role=checkbox]')?.getAttribute('aria-checked') === 'true'), 150));
        }
        """;

    /// <summary>
    /// Cada campo visível do formulário com o rótulo de verdade (for, rótulo em volta, aria-labelledby ou aria-label) e, no rádio, a
    /// pergunta do grupo. Scope picks the form; near also reads the text right before a field that has no label of its own.
    /// </summary>
    private const string Discover = """
        ([scope, near]) => {
          const form = document.querySelector(scope); if (!form) return '[]';
          const text = (el) => (el?.textContent || '').replace(/SVGs not supported by this browser\./g, ' ').replace(/\s+/g, ' ').trim();
          const byIds = (ids) => (ids || '').split(/\s+/).map(i => text(document.getElementById(i))).join(' ').trim();
          const labelOf = (e) => {
            if (e.id) { const l = document.querySelector('label[for="' + CSS.escape(e.id) + '"]'); if (l && text(l)) return text(l); }
            const w = e.closest('label'); if (w && text(w)) return text(w);
            // Sem rótulo comum: o texto ligado por aria, o texto logo antes do campo, o exemplo do campo ou, por último, o nome interno dele.
            return byIds(e.getAttribute('aria-labelledby')) || e.getAttribute('aria-label') || (near && (e.type === 'checkbox' || e.type === 'radio') ? after(e) : '')
              || (near ? before(e) : '') || e.getAttribute('placeholder') || (e.name || '').replace(/[_-]+/g, ' ');
          };
          // A box's text usually comes right after it ("[ ] I agree to the terms").
          const after = (e) => {
            let p = e.nextSibling;
            while (p && !(p.textContent || '').trim()) p = p.nextSibling;
            const t = p && !(p.querySelector && p.querySelector('input, select, textarea')) ? (p.textContent || '').replace(/\s+/g, ' ').trim() : '';
            return t.length <= 300 ? t : '';
          };
          // The text a person reads just before the field, a few levels up, when that text is short and holds no other field.
          const before = (e) => {
            for (let n = e, up = 0; n && up < 6; n = n.parentElement, up++) {
              let p = n.previousElementSibling;
              while (p && !text(p)) p = p.previousElementSibling;
              if (p && !p.querySelector('input, select, textarea')) { const t = text(p); if (t.length > 0 && t.length <= 160) return t; }
              if (p) return '';
            }
            return '';
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
            // Hidden copies that size a text box (aria-hidden, read only) are not questions.
            if (type !== 'file' && type !== 'radio' && type !== 'checkbox' && (e.readOnly || e.getAttribute('aria-hidden') === 'true')) continue;
            if (!e.id) e.id = 'jo-' + Math.random().toString(36).slice(2, 10);
            const label = labelOf(e);
            // Many sites mark a required field only with an asterisk or "(obrigatório)" in its label, not in the HTML.
            const marked = /\*\s*:?\s*$/.test(label) || /\((obrigat[oó]ri[oa]|required)\)/i.test(label);
            out.push({ id: e.id, name: e.name || '', tag: e.tagName.toLowerCase(), type, required: !!e.required || e.getAttribute('aria-required') === 'true' || marked,
              label, group: type === 'radio' || type === 'checkbox' ? groupOf(e) : '', nth: type === 'file' ? files++ : 0 });
          }
          return JSON.stringify(out);
        }
        """;

    protected async Task<IReadOnlyList<Found>> DiscoverAsync(IPage page, string scope, bool near) =>
        JsonSerializer.Deserialize<Found[]>(await page.EvaluateAsync<string>(Discover, new object[] { scope, near }), JsonSerializerOptions.Web) ?? [];

    /// <summary>Fills the discovered fields inside scope (a CSS selector of the form) and reads them back.</summary>
    protected async Task<AdapterResult<PreparedForm>> FillFoundAsync(IPage page, ApplyTarget target, MaterialBundle m, IReadOnlyList<Found> found, string scope)
    {
        var fields = new List<FieldValue>();
        var missing = new List<FormQuestion>();
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
                    if (group.Any(o => o.Required)) missing.Add(new FormQuestion(question.Length > 0 ? question : label, "radio", [.. group.Select(o => Clean(o.Label))]));
                    continue;
                }
                // A opção do Workable é um bloco (role=radio) com o rádio de verdade escondido dentro: clica no bloco, como uma
                // pessoa, e confere a marca nele (aria-checked). Sem bloco, marca pelo nome do grupo e pelo texto da opção.
                if (!await PickAsync(page, f.Name, Clean(choice.Label)))
                    return AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "fill-mismatch", $"A resposta de \"{question}\" não ficou marcada.");
                fields.Add(new FieldValue(question, Clean(choice.Label), answer!.Value.Source));
                continue;
            }
            if (f.Type == "checkbox" && (group.Count() > 1 || Clean(f.Group) is { Length: > 0 } asked && !Declaration().IsMatch(asked) && !Declaration().IsMatch(label)))
            {
                // Lista para marcar (quais tecnologias você usa, por exemplo): marca as opções da sua resposta ou da que a IA escreveu
                // pelos fatos do perfil. Opcional e sem resposta, fica em branco.
                var question = Clean(f.Group.Length > 0 ? f.Group : label);
                var options = group.Select(o => (Box: o, Text: Clean(o.Label))).ToList();
                if (Mine(question, m) is not { } chosen)
                {
                    if (group.Any(o => o.Required)) missing.Add(new FormQuestion(question, "checkboxes", [.. options.Select(o => o.Text)]));
                    continue;
                }
                var picks = chosen.Value.Split(FormQuestion.ChoiceSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(c => options.FirstOrDefault(o => Picks(o.Text, c))).Where(o => o.Box is not null).Distinct().ToList();
                if (picks.Count == 0)
                {
                    if (group.Any(o => o.Required)) missing.Add(new FormQuestion(question, "checkboxes", [.. options.Select(o => o.Text)]));
                    continue;
                }
                foreach (var pick in picks)
                    if (!await page.EvaluateAsync<bool>(TickBox, pick.Box.Id))
                        return AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "fill-mismatch", $"A opção \"{pick.Text}\" de \"{question}\" não ficou marcada.");
                fields.Add(new FieldValue(question, string.Join(", ", picks.Select(p => p.Text)), chosen.Source));
                continue;
            }
            if (f.Type == "checkbox" || Declaration().IsMatch(label))
            {
                if (!f.Required && !Agreeable(label, m)) continue;
                if (Tick(label, m) is not { } tick)
                {
                    if (Consent().IsMatch(label)) return NeedsDeclaration(label);
                    missing.Add(new FormQuestion(label, "confirm", []));
                    continue;
                }
                if (!await page.EvaluateAsync<bool>(TickBox, f.Id))
                    return AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "fill-mismatch", $"A caixa \"{(label.Length > 80 ? label[..80] + "…" : label)}\" não ficou marcada.");
                fields.Add(new FieldValue(label, tick.Value, tick.Source));
                continue;
            }
            if (f.Type == "file")
            {
                if (f.Nth != resumeAt)
                {
                    if (f.Required) return AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "file", $"O formulário pede outro arquivo além do currículo: \"{label}\". Fica com você.");
                    continue;
                }
                await page.Locator($"{scope} input[type=file]").Nth(f.Nth).SetInputFilesAsync(m.ResumePdfPath);
                fields.Add(new FieldValue("Currículo", $"{System.IO.Path.GetFileName(m.ResumePdfPath)} (sha256 {FileHash(m.ResumePdfPath)[..12]})", "currículo"));
                continue;
            }
            var (value, source) = Resolve(label, f.Type, m);
            var control = page.Locator(f.Name.Length > 0 ? $"{scope} [name='{f.Name.Replace("'", "\\'")}']" : Css(f.Id)).First;
            if (f.Tag == "select")
            {
                var options = (await control.Locator("option").AllInnerTextsAsync()).Select(o => o.Trim()).Where(o => o.Length > 0 && !o.StartsWith("Select", StringComparison.OrdinalIgnoreCase)).ToList();
                var match = value is null ? null : options.FirstOrDefault(o => SameAnswer(o, value));
                if (match is null) { if (f.Required) missing.Add(new FormQuestion(label, "select", options)); continue; }
                await control.SelectOptionAsync(new SelectOptionValue { Label = match });
                fields.Add(new FieldValue(label, match, source));
                continue;
            }
            if (value is not null && PersonalAnswer.IsBirthDate(label))
                value = PersonalAnswer.FormatBirthDate(value, label, f.Type, await control.GetAttributeAsync("placeholder"));
            if (f.Type == "number" && value is not null) value = Digits(value);
            if (string.IsNullOrEmpty(value))
            {
                if (f.Required) missing.Add(new FormQuestion(label, PersonalAnswer.IsBirthDate(label) ? "date" : f.Tag == "textarea" ? "textarea" : f.Type == "number" ? "number" : "text", []));
                continue;
            }
            await control.FillAsync(value);
            var got = await control.InputValueAsync();
            // O telefone ganha máscara do país: vale se os dígitos batem.
            if (f.Type == "tel" ? Last8(got) != Last8(value) : got != value)
            {
                // A field with a mask of its own (a salary that takes only the number) keeps the digits: typed as digits, the same number counts.
                var digits = Digits(value);
                if (AllowsNumericMask(f.Tag, f.Type, label) && digits.Length > 0)
                {
                    if (Digits(got) != digits) { await control.FillAsync(digits); got = await control.InputValueAsync(); }
                    if (Digits(got) == digits || Digits(got) == digits + "00") { fields.Add(new FieldValue(label, got, source)); continue; }
                }
                return AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "fill-mismatch", $"O campo \"{label}\" não guardou o valor.");
            }
            fields.Add(new FieldValue(label, value, source));
        }
        if (missing.Count > 0) return Missing(missing);
        if (fields.Count == 0) return AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "no-fields", "Nenhum campo com rótulo foi reconhecido.");
        return AdapterResult<PreparedForm>.Ok(new PreparedForm(Id, Version, new Uri(target.ApplyUrl).Authority, PayloadHash(fields), fields));
    }

    // A salary mask can strip currency text. The same digits in a name or free-text answer do not preserve the answer.
    internal static bool AllowsNumericMask(string tag, string type, string label) => tag == "input"
        && (type == "number" || type == "text" && Regex.IsMatch(label, @"sal[aá]r|pretens[aã]o|remunera|salary|compensation|expected pay|pay expectation", RegexOptions.IgnoreCase));

    protected static async Task<bool> PickAsync(IPage page, string name, string label)
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
    protected static (string Value, string Source)? YesNo(string q, MaterialBundle m)
    {
        if (Mine(q, m) is { } mine) return mine;
        if (AtLeastYears(q, m) is { } years) return years;
        if (MinYears().IsMatch(q)) return null;
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
    protected static bool Picks(string option, string answer)
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

    protected static string Css(string id) => $"[id='{id.Replace("'", "\\'")}']";
    protected static string Last8(string s) { var d = new string([.. s.Where(char.IsDigit)]); return d.Length > 8 ? d[^8..] : d; }

    [GeneratedRegex(@"\bI (confirm|agree|accept|declare|certify|acknowledge)\b|\bli e (concordo|aceito)|\bdeclaro\b|\bconcordo com\b|\bcertifico\b", RegexOptions.IgnoreCase)] private static partial Regex Declaration();
    [GeneratedRegex(@"english|ingl[eê]s", RegexOptions.IgnoreCase)] private static partial Regex English();
    [GeneratedRegex(@"cover letter|carta", RegexOptions.IgnoreCase)] private static partial Regex Cover();
    [GeneratedRegex(@"\b(cv|resume|curr[ií]culo)\b.*\b(in english|em ingl[eê]s)\b", RegexOptions.IgnoreCase)] private static partial Regex CvInEnglish();
    [GeneratedRegex(@"\b(located|based|living|reside|resident|mora|reside)\b.*?\bin\s+(?<where>[^?(]+)|\b(mora|reside) n[oa]\s+(?<where>[^?(]+)", RegexOptions.IgnoreCase)] private static partial Regex Located();
    [GeneratedRegex(@"latin america|latam|am[eé]rica latina|south america|am[eé]rica do sul", RegexOptions.IgnoreCase)] private static partial Regex LatAm();
    [GeneratedRegex(@"\bbra[sz]il\b", RegexOptions.IgnoreCase)] private static partial Regex Brazil();
    [GeneratedRegex(@"^(no|nao|n|false|falso)$")] private static partial Regex No();
}
