using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Base dos formulários de candidatura preenchidos por rótulo (getByLabel), nunca por seletor frágil: o id do campo pode mudar a cada
/// carga que o rótulo continua o mesmo. Prepara sem clicar em enviar; envia só com token válido e payload igual ao aprovado; reconcilia
/// só lendo. Campo que aparece depois de uma resposta (pergunta condicional) entra numa segunda passada. CAPTCHA, login e MFA param
/// para a pessoa. A declaração obrigatória (privacidade, termos) só é marcada quando a sua autorização aceita as declarações.
/// </summary>
internal abstract partial class LabelForm(BrowserPool browsers, IHttpClientFactory http, GuardKey key, TimeProvider time, IOptions<AutomationOptions> options) : IFormAdapter
{
    public abstract string Id { get; }
    public string Version => "1.1.0";
    public abstract string Name { get; }
    public string Kind => "Navegador (Playwright)";
    public double Risk => 0.3;
    public IReadOnlyList<Capability> Capabilities { get; } = [Capability.PrepareForm, Capability.SubmitApplication, Capability.ReconcileSubmission];
    protected abstract string[] RealDomains { get; }
    /// <summary>No ATS falso, cada formato mora num caminho próprio do mesmo host.</summary>
    protected abstract Regex FakePath { get; }
    public IReadOnlyList<string> Domains => [.. RealDomains, .. options.Value.FakeAtsHosts];
    public IReadOnlyList<string> LiveDomains => RealDomains;
    public bool CanReconcile(Uri url) => options.Value.FakeAtsHosts.Contains(url.Authority, StringComparer.OrdinalIgnoreCase);
    protected AutomationOptions Options => options.Value;

    public virtual double Fit(Uri url) =>
        RealDomains.Any(d => d.Equals(url.Host, StringComparison.OrdinalIgnoreCase)) ? 1
        : options.Value.FakeAtsHosts.Contains(url.Authority, StringComparer.OrdinalIgnoreCase) && FakePath.IsMatch(url.AbsolutePath) ? 1 : 0;

    /// <summary>Pergunta conhecida: o padrão do rótulo e a chave da resposta aprovada.</summary>
    // Chaves do banco de respostas (Profile.Contracts.AnswerKeys). Anos com tecnologia: years_experience:{tecnologia}.
    protected static readonly (Regex Label, string Answer)[] Questions =
    [
        (new Regex(@"authori[sz]ed to work|autoriza", RegexOptions.IgnoreCase), "work_authorization"),
        (new Regex(@"sponsorship|visto|patroc", RegexOptions.IgnoreCase), "needs_sponsorship"),
        (new Regex(@"(sal[aá]r|pretens|remunera|compensation|\bpay\b).*(\busd\b|us\$|dollars?|d[oó]lar)|(\busd\b|us\$|dollars?|d[oó]lar).*(sal[aá]r|pretens|remunera|compensation|\bpay\b)", RegexOptions.IgnoreCase), "salary_usd"),
        (new Regex(@"(sal[aá]r|pretens|remunera|compensation).*\bPJ\b|\bPJ\b.*(sal[aá]r|pretens|remunera)", RegexOptions.IgnoreCase), "salary_pj"),
        (new Regex(@"(sal[aá]r|pretens|remunera|compensation).*\bCLT\b|\bCLT\b.*(sal[aá]r|pretens|remunera)", RegexOptions.IgnoreCase), "salary_clt"),
        (new Regex(@"sal[aá]r|pretens|compensation|remunera|expected pay|pay expectation", RegexOptions.IgnoreCase), "salary_expectation"),
        (new Regex(@"notice period|aviso pr|start date|quando (você )?pode(ria)? começar|disponibilidade (de |para )?(in[ií]cio|come[cç]ar)|data de in[ií]cio|availability to start", RegexOptions.IgnoreCase), "start_date"),
        (new Regex(@"english|ingl", RegexOptions.IgnoreCase), "english_level"),
        (new Regex(@"hear about|soube da vaga", RegexOptions.IgnoreCase), "how_heard"),
        (new Regex(@"relocat|mudan[cç]a", RegexOptions.IgnoreCase), "relocation"),
        (new Regex(@"(which|what) city|current location|where are you (currently )?based|based in|cidade|onde (você )?mora|localiza|^address$|^endere[cç]o", RegexOptions.IgnoreCase), "location"),
        (new Regex(@"github", RegexOptions.IgnoreCase), "github_url"),
        (new Regex(@"portf[oó]lio|personal (url|website|site)|url pessoal|site pessoal|^website$|^site$", RegexOptions.IgnoreCase), "portfolio_url"),
        (new Regex(@"^headline$|^t[ií]tulo profissional", RegexOptions.IgnoreCase), "headline"),
        (new Regex(@"^summary$|^resumo( profissional)?$|^about you$|^sobre voc[eê]", RegexOptions.IgnoreCase), "summary"),
    ];

    private static readonly Regex YearsWith = new(@"(?:years|anos)\b.*?\b(?:with|in|de|com|em)\s+([\w#.+]+(?:\s[\w#.+]+)?)", RegexOptions.IgnoreCase);

    public async Task<AdapterResult<PreparedForm>> PrepareAsync(ApplyTarget target, MaterialBundle materials, CancellationToken ct)
    {
        await using var lease = await LeaseAsync(target, ct);
        var page = await lease.Context.NewPageAsync();
        var filled = await FillAsync(page, target, materials);
        // A failure goes back as it came, with the questions the form asked: they are what the AI answers next.
        return filled.Kind == AdapterResultKind.Completed ? AdapterResult<PreparedForm>.Ok(filled.Value!, "Formulário preenchido e conferido. Nada foi enviado.") : filled;
    }

    public async Task<AdapterResult<SubmissionReceipt>> SubmitAsync(ApplyTarget target, MaterialBundle materials, SubmitToken token, CancellationToken ct)
    {
        if (token.ApplicationId != target.ApplicationId || !token.IsValid(key.Bytes, time.GetUtcNow().UtcDateTime))
            return AdapterResult<SubmissionReceipt>.Fail(AdapterResultKind.BlockedByPolicy, "invalid-token", "Autorização de envio inválida ou vencida.");
        await using var lease = await LeaseAsync(target, ct);
        var page = await lease.Context.NewPageAsync();
        var filled = await FillAsync(page, target, materials);
        if (filled.Kind != AdapterResultKind.Completed)
            return AdapterResult<SubmissionReceipt>.Fail(filled.Kind, filled.ReasonCode!, filled.Detail);
        if (filled.Value!.PayloadHash != token.PayloadHash)
            return AdapterResult<SubmissionReceipt>.Fail(AdapterResultKind.BlockedByPolicy, "payload-changed", "O formulário mudou desde a aprovação. Nada foi enviado.");

        // Instructions and hidden success templates are not evidence that this submission arrived.
        var priorConfirmations = await VisibleTextAsync(page.GetByText(Confirmed));
        var priorDuplicates = await VisibleTextAsync(page.GetByText(AlreadyApplied()));
        var priorAlerts = await VisibleTextAsync(page.Locator("[role=alert]"));
        var priorReceipts = await VisibleTextAsync(page.GetByText(ReceiptId()));
        Chaos.Point("before-click");
        try
        {
            await SubmitControl(page).ClickAsync();
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // O clique pode ter saído antes da falha: só a reconciliação decide.
            return AdapterResult<SubmissionReceipt>.Fail(AdapterResultKind.SubmissionUnknown, "click-uncertain", "O clique de envio falhou no meio; vai para reconciliação.");
        }
        Chaos.Point("after-click");

        var waiting = System.Diagnostics.Stopwatch.StartNew();
        string[] confirmations = [], duplicates = [], alerts = [];
        do
        {
            ct.ThrowIfCancellationRequested();
            confirmations = (await VisibleTextAsync(page.GetByText(Confirmed))).Except(priorConfirmations, StringComparer.Ordinal).ToArray();
            duplicates = (await VisibleTextAsync(page.GetByText(AlreadyApplied()))).Except(priorDuplicates, StringComparer.Ordinal).ToArray();
            alerts = (await VisibleTextAsync(page.Locator("[role=alert]"))).Except(priorAlerts, StringComparer.Ordinal).ToArray();
            if (confirmations.Length > 0 || duplicates.Length > 0 || alerts.Length > 0) break;
            await Task.Delay(100, ct);
        }
        while (waiting.ElapsedMilliseconds < browsers.TimeoutMs);

        if (duplicates.Length > 0)
            return AdapterResult<SubmissionReceipt>.Fail(AdapterResultKind.PermanentFailure, "already-applied", "O ATS recusou: já existe candidatura com este e-mail.");
        if (confirmations.Length == 0 && alerts.Length > 0)
            return AdapterResult<SubmissionReceipt>.Fail(AdapterResultKind.PermanentFailure, "ats-validation", "O ATS recusou o formulário e mostrou erro de validação.");
        if (confirmations.Length == 0)
            return AdapterResult<SubmissionReceipt>.Fail(AdapterResultKind.SubmissionUnknown, "no-confirmation", "Clicou em enviar, mas nenhuma nova confirmação visível apareceu. O envio precisa de reconciliação.");
        var newReceipts = (await VisibleTextAsync(page.GetByText(ReceiptId()))).Except(priorReceipts, StringComparer.Ordinal);
        var receipt = ReceiptId().Match(string.Join('\n', confirmations.Concat(newReceipts)));
        return receipt.Success
            ? AdapterResult<SubmissionReceipt>.Ok(new SubmissionReceipt(receipt.Groups[1].Value, 4, "Página de confirmação com número do recibo."))
            : AdapterResult<SubmissionReceipt>.Ok(new SubmissionReceipt("", 3, "Página de confirmação reconhecida, sem número de recibo."));
    }

    private static async Task<string[]> VisibleTextAsync(ILocator locator)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Math.Min(await locator.CountAsync(), 100); i++)
        {
            var node = locator.Nth(i);
            if (await node.IsVisibleAsync()) result.Add(Regex.Replace(await node.InnerTextAsync(), @"\s+", " ").Trim());
        }
        return [.. result.Where(t => t.Length > 0)];
    }

    /// <summary>Só leitura: pergunta ao portal do ATS se existe candidatura deste e-mail para esta vaga.</summary>
    public async Task<AdapterResult<ReconcileResult>> ReconcileAsync(ApplyTarget target, MaterialBundle materials, CancellationToken ct)
    {
        var uri = new Uri(target.ApplyUrl);
        var job = JobId().Match(uri.AbsolutePath);
        if (!job.Success || !options.Value.FakeAtsHosts.Contains(uri.Authority, StringComparer.OrdinalIgnoreCase))
            return AdapterResult<ReconcileResult>.Fail(AdapterResultKind.NeedsHuman, "no-portal", "Este ATS não tem consulta de candidatura; confira o e-mail de confirmação.");
        var url = $"{uri.Scheme}://{uri.Authority}/api/receipts?job={job.Groups[1].Value}&email={Uri.EscapeDataString(materials.Email)}";
        using var response = await http.CreateClient("sources").GetAsync(url, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return AdapterResult<ReconcileResult>.Ok(new ReconcileResult(false, null, "O ATS não tem candidatura deste e-mail para a vaga."));
        response.EnsureSuccessStatusCode();
        using var doc = await HttpBodies.JsonAsync(response.Content, ct);
        var id = doc.RootElement.GetProperty("receiptId").GetString();
        return AdapterResult<ReconcileResult>.Ok(new ReconcileResult(true, id, "O ATS confirma a candidatura."));
    }

    /// <summary>Hosts de arquivos do próprio ATS (CDN, depósito de arquivos) que a página precisa para abrir o formulário; nunca o de verificação anti-robô.</summary>
    protected virtual string[] AssetHosts => [];
    private IReadOnlyCollection<string> Hosts(ApplyTarget target) => [new Uri(target.ApplyUrl).Authority, .. Domains, .. AssetHosts];

    /// <summary>A clean browser context that only reaches the hosts this form needs.</summary>
    protected virtual Task<BrowserLease> LeaseAsync(ApplyTarget target, CancellationToken ct) => browsers.LeaseAsync(Hosts(target), ct);

    /// <summary>The button that sends the filled form.</summary>
    protected virtual ILocator SubmitControl(IPage page) => page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { NameRegex = SubmitButton() }).First;

    /// <summary>The words of the page that says the application arrived.</summary>
    protected virtual Regex Confirmed => Confirmation();

    internal static bool SaysSubmitted(string text) => Confirmation().IsMatch(text);

    protected BrowserPool Browsers => browsers;
    /// <summary>CAPTCHA e verificação anti-robô (reCAPTCHA, hCaptcha, Cloudflare Turnstile): o robô para e a vaga fica com você.</summary>
    protected const string BotCheck = "[data-sitekey], .g-recaptcha, .h-captcha, .cf-turnstile, iframe[src*='captcha'], iframe[src*='challenges.cloudflare.com'], [data-captcha]";

    /// <summary>Abre, confere bloqueios, preenche por rótulo (em até três passadas, para campo condicional) e lê de volta. Mesmo caminho no preparo e no envio.</summary>
    protected virtual async Task<AdapterResult<PreparedForm>> FillAsync(IPage page, ApplyTarget target, MaterialBundle m)
    {
        var response = await page.GotoAsync(target.ApplyUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (response is null || response.Status is 404 or 410)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.PermanentFailure, "posting-closed", "A vaga não está mais no ar.");
        if (await page.Locator(BotCheck).CountAsync() > 0)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "captcha", "A página tem CAPTCHA ou verificação anti-robô: o app não passa por ela, o envio fica com você.");
        if (await page.Locator("input[autocomplete='one-time-code'], input[name*='otp' i], input[name*='mfa' i]").CountAsync() > 0 || await page.GetByText(Mfa()).CountAsync() > 0)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "mfa", "A página pede um código de verificação (MFA). Só você recebe e digita.");
        if (await page.Locator("input[type=password]").CountAsync() > 0)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "login", "A página pede login. Fica para a sessão assistida.");
        if (await page.Locator("form").CountAsync() == 0)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "no-form", "Não há formulário reconhecível na página.");
        var fields = new List<FieldValue>();
        var done = new HashSet<string>();
        // Pergunta obrigatória sem resposta não para na primeira: o ensaio mostra todas de uma vez, para você responder numa ida só.
        var missing = new List<FormQuestion>();
        for (var pass = 0; pass < 3; pass++)
        {
            var before = done.Count;
            foreach (var label in await page.Locator("form label[for]").AllAsync())
            {
                var id = await label.GetAttributeAsync("for") ?? "";
                if (id.Length == 0 || done.Contains(id)) continue;
                var control = page.Locator($"[id='{id}']");
                if (await control.CountAsync() == 0 || !await control.IsVisibleAsync()) continue;
                done.Add(id);
                var text = Clean(await label.InnerTextAsync());
                var tag = await control.EvaluateAsync<string>("e => e.tagName.toLowerCase()");
                var type = (await control.GetAttributeAsync("type"))?.ToLowerInvariant() ?? "";
                var required = await control.EvaluateAsync<bool>("e => e.required");
                if (type == "checkbox")
                {
                    // Optional boxes (newsletter, future openings) stay blank. A required one is a declaration or a fact about you.
                    if (!required && !Agreeable(text, m)) continue;
                    if (Tick(text, m) is not { } tick)
                    {
                        if (Consent().IsMatch(text)) return NeedsDeclaration(text);
                        missing.Add(new FormQuestion(text, "confirm", []));
                        continue;
                    }
                    await control.CheckAsync();
                    fields.Add(new FieldValue(text, tick.Value, tick.Source));
                    continue;
                }
                var (value, source) = Resolve(text, type, m);
                if (value is not null && PersonalAnswer.IsBirthDate(text))
                    value = PersonalAnswer.FormatBirthDate(value, text, type, await control.GetAttributeAsync("placeholder"));
                if (tag == "select")
                {
                    var options = (await control.Locator("option").AllInnerTextsAsync()).Select(o => o.Trim()).Where(o => o.Length > 0 && !Placeholder().IsMatch(o)).ToList();
                    var match = value is null ? null : options.FirstOrDefault(o => SameAnswer(o, value));
                    // "At least 2 years with C#?" with Yes and No to pick: the years from the experiences' dates decide.
                    if (match is null && AtLeastYears(text, m) is { } years && options.FirstOrDefault(o => SameAnswer(o, years.Value)) is { } picked)
                        (match, source) = (picked, years.Source);
                    if (match is null)
                    {
                        if (required) missing.Add(new FormQuestion(text, "select", options));
                        continue;
                    }
                    await control.SelectOptionAsync(new SelectOptionValue { Label = match });
                    fields.Add(new FieldValue(text, match, source));
                    continue;
                }
                if (type == "number" && value is not null) value = Digits(value);
                if (string.IsNullOrEmpty(value))
                {
                    if (required) missing.Add(new FormQuestion(text, PersonalAnswer.IsBirthDate(text) ? "date" : tag == "textarea" ? "textarea" : type == "number" ? "number" : "text", []));
                    continue;
                }
                if (type == "file")
                {
                    await control.SetInputFilesAsync(value);
                    fields.Add(new FieldValue(text, $"{Path.GetFileName(value)} (sha256 {FileHash(value)[..12]})", source));
                }
                else
                {
                    await control.FillAsync(value);
                    if (await control.InputValueAsync() != value)
                        return AdapterResult<PreparedForm>.Fail(AdapterResultKind.RetryableFailure, "fill-mismatch", $"O campo \"{text}\" não guardou o valor.");
                    fields.Add(new FieldValue(text, value, source));
                }
            }
            if (done.Count == before) break;
            // Uma resposta pode ter aberto outra pergunta: dá tempo de a página mostrar antes da próxima passada.
            await page.WaitForTimeoutAsync(150);
        }
        if (missing.Count > 0) return Missing(missing);
        // A required box with no label of its own was never seen by the loop: the form would refuse to send, so it stops here.
        if (await page.Locator("form input[type=checkbox][required]:not(:checked)").CountAsync() > 0)
            return NeedsDeclaration(Clean(await page.Locator("form input[type=checkbox][required]:not(:checked)").First.EvaluateAsync<string>("e => (e.closest('label, p, div') || e).textContent")));
        if (fields.Count == 0) return AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "no-fields", "Nenhum campo com rótulo foi reconhecido.");
        var hash = PayloadHash(fields);
        return AdapterResult<PreparedForm>.Ok(new PreparedForm(Id, Version, new Uri(target.ApplyUrl).Authority, hash, fields));
    }

    private static readonly string[][] Equivalents =
    [
        ["Sim", "Yes", "Sí"], ["Não", "Nao", "No"],
        ["Prefiro não informar", "Prefiro não responder", "I don't wish to answer", "I do not wish to answer", "Decline to self-identify", "Prefer not to say"],
    ];

    /// <summary>
    /// An option is the approved answer in the form's own language too: a form in English gets "Yes" for an approved "Sim". Brazilian
    /// companies often write the job in Portuguese and keep the ATS's standard questions in English.
    /// </summary>
    internal static bool SameAnswer(string option, string answer)
    {
        var o = option.Trim();
        var a = answer.Trim();
        return o.Equals(a, StringComparison.OrdinalIgnoreCase)
            || Equivalents.Any(g => g.Contains(o, StringComparer.OrdinalIgnoreCase) && g.Contains(a, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// As perguntas obrigatórias sem resposta, todas no detalhe, entre aspas: a tela do ensaio abre um campo para cada uma. The questions
    /// also go back whole (kind and options), for the AI to answer them from your profile before the form is tried again.
    /// </summary>
    protected static AdapterResult<PreparedForm> Missing(IReadOnlyList<FormQuestion> questions) => AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "unsupported-field",
        $"{(questions.Count == 1 ? "Pergunta obrigatória sem resposta aprovada" : "Perguntas obrigatórias sem resposta aprovada")}: {string.Join(" | ", questions.Select(q => $"\"{q.Label}\""))}.") with { Questions = questions };

    /// <summary>A privacy, data processing or terms box: only your authorization ticks it, never a guess.</summary>
    protected static AdapterResult<PreparedForm> NeedsDeclaration(string label) => AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "declaration",
        $"O formulário pede uma declaração: \"{(label.Length > 120 ? label[..120] + "…" : label)}\". Para o app marcar em seu nome, ligue \"aceito as declarações\" na autorização; sem isso, só você marca.");

    /// <summary>
    /// Whether a required box gets ticked, and why: a privacy, data or terms declaration when your authorization accepts them, or a
    /// statement about you that your answers (or the AI's, from your profile) confirm. Null leaves it for the AI or for you.
    /// </summary>
    protected static (string Value, string Source)? Tick(string label, MaterialBundle m)
    {
        if (Marketing().IsMatch(label)) return null;
        if (Consent().IsMatch(label)) return m.AcceptDeclarations ? ("Aceito", "sua autorização (declarações)") : null;
        return Mine(label, m) is { } mine && Yes().IsMatch(CustomAnswer.Normalize(mine.Value)) ? ("Marcado", mine.Source) : null;
    }

    /// <summary>
    /// An optional privacy or terms box your authorization accepts: many sites check it only when the form arrives, so it gets ticked
    /// like a required one. Newsletters, job alerts and talent pools are never ticked unless the form requires them.
    /// </summary>
    protected static bool Agreeable(string label, MaterialBundle m) => m.AcceptDeclarations && Consent().IsMatch(label) && !Marketing().IsMatch(label);

    /// <summary>Your answer to this exact question or, without one, the answer the AI wrote from your profile for this form.</summary>
    protected static (string Value, string Source)? Mine(string question, MaterialBundle m) =>
        m.Answers.TryGetValue(CustomAnswer.Key(question), out var mine) ? (mine, "sua resposta")
        : m.Answers.TryGetValue(CustomAnswer.AiKey(question), out var ai) ? (ai, "IA, pelos fatos do seu perfil") : null;

    /// <summary>
    /// The years key for the technology a question names: the same name, or the name followed by a word that does not narrow it
    /// ("C# development", ".NET e"). "React Native" is not React and ".NET WebForms" is not .NET: those go to the AI with the facts.
    /// </summary>
    internal static string? TechKey(string tech, MaterialBundle m)
    {
        var words = tech.Trim().TrimEnd('?', '.', ',', ';', ':', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;
        var name = words.Length > 1 && Loose().IsMatch(words[^1]) ? string.Join(' ', words[..^1]) : string.Join(' ', words);
        return m.Answers.Keys.FirstOrDefault(k => k.StartsWith("years_experience:", StringComparison.Ordinal) && string.Equals(k[17..], name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// "Do you have at least 2 years with C#?": yes or no from the years the experiences' dates give for exactly that technology. Null
    /// when the question asks no minimum or names a technology the profile has no dates for: the AI answers it from the facts.
    /// </summary>
    internal static (string Value, string Source)? AtLeastYears(string question, MaterialBundle m)
    {
        if (MinYears().Match(question) is not { Success: true } y || !int.TryParse(y.Groups["n"].Value, out var need)) return null;
        var key = TechKey(y.Groups["tech"].Value, m);
        return key is not null && int.TryParse(m.Answers[key], out var have) ? (have >= need ? "Yes" : "No", $"{have} anos de {key[17..]} pelas datas das experiências") : null;
    }

    protected static string Digits(string s) => new([.. s.Where(char.IsDigit)]);

    internal static (string? Value, string Source) Resolve(string label, string type, MaterialBundle m)
    {
        if (type == "file") return Resume().IsMatch(label) ? (m.ResumePdfPath, "currículo") : (null, "");
        if (PersonalAnswer.IsBirthDate(label))
            return m.Answers.TryGetValue(CustomAnswer.Key(label), out var birth) && PersonalAnswer.TryBirthDate(birth, DateOnly.FromDateTime(DateTime.UtcNow), out _)
                ? (birth, "sua resposta pessoal") : (null, "");
        if (FullName().IsMatch(label)) return ($"{m.FirstName} {m.LastName}".Trim(), "perfil");
        if (FirstName().IsMatch(label)) return (m.FirstName, "perfil");
        if (LastName().IsMatch(label)) return (m.LastName, "perfil");
        if (Email().IsMatch(label) || type == "email") return (m.Email, "perfil");
        if (Phone().IsMatch(label) || type == "tel") return (m.Phone, "perfil");
        if (LinkedIn().IsMatch(label)) return (m.LinkedIn, "perfil");
        if (Mine(label, m) is { } mine) return mine;
        // "At least 2 years with C#?" written as a text box is a yes or no; "how many years with C#?" wants the number.
        if (AtLeastYears(label, m) is { } atLeast) return (Portuguese().IsMatch(label) ? atLeast.Value == "Yes" ? "Sim" : "Não" : atLeast.Value, atLeast.Source);
        if (MinYears().IsMatch(label)) return (null, "");
        if (YearsWith.Match(label) is { Success: true } years)
            return TechKey(years.Groups[1].Value, m) is { } key ? (m.Answers[key], "anos pelas datas das experiências") : (null, "");
        // Your current pay is not your floor: the profile does not say it, so it is yours to answer once.
        if (CurrentPay().IsMatch(label)) return (null, "");
        foreach (var (pattern, answer) in Questions)
            if (pattern.IsMatch(label)) return m.Answers.TryGetValue(answer, out var v) ? (v, "banco de respostas") : (null, "");
        return (null, "");
    }

    /// <summary>Hash do que vai ser enviado: o mesmo formulário com os mesmos valores gera o mesmo hash.</summary>
    internal static string PayloadHash(IEnumerable<FieldValue> fields)
    {
        var canonical = string.Join("\n", fields.OrderBy(f => f.Label, StringComparer.Ordinal).Select(f => $"{f.Label}={f.Value}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    protected static string FileHash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    // O texto de ícone que a página põe dentro do rótulo ("SVGs not supported by this browser.") não é parte da pergunta.
    protected static string Clean(string label) => Spaces().Replace(label.Replace("*", "").Replace("SVGs not supported by this browser.", " "), " ").Trim();

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"^full name|^name$|^nome completo|^seu nome", RegexOptions.IgnoreCase)] private static partial Regex FullName();
    [GeneratedRegex(@"^first name|^nome$|^primeiro nome", RegexOptions.IgnoreCase)] private static partial Regex FirstName();
    [GeneratedRegex(@"^last name|sobrenome", RegexOptions.IgnoreCase)] private static partial Regex LastName();
    [GeneratedRegex(@"^e-?mail", RegexOptions.IgnoreCase)] private static partial Regex Email();
    [GeneratedRegex(@"phone|telefone|celular", RegexOptions.IgnoreCase)] protected static partial Regex Phone();
    [GeneratedRegex(@"linkedin", RegexOptions.IgnoreCase)] private static partial Regex LinkedIn();
    [GeneratedRegex(@"resume|cv|curr[ií]culo", RegexOptions.IgnoreCase)] protected static partial Regex Resume();
    [GeneratedRegex(@"submit|send application|enviar candidatura", RegexOptions.IgnoreCase)] private static partial Regex SubmitButton();
    // Workable says "Your application has been submitted successfully."
    [GeneratedRegex(@"thank(s| you)( so much)? for (applying|your application)|application (has been |was )?(received|submitted|sent)|submitted successfully|candidatura (foi )?(recebida|enviada)", RegexOptions.IgnoreCase)]
    private static partial Regex Confirmation();
    [GeneratedRegex(@"already (applied|submitted)|j[aá] se candidatou", RegexOptions.IgnoreCase)] private static partial Regex AlreadyApplied();
    [GeneratedRegex(@"verification code|c[oó]digo de verifica[cç][aã]o|two-factor|autentica[cç][aã]o em dois", RegexOptions.IgnoreCase)] protected static partial Regex Mfa();
    [GeneratedRegex(@"application id:?\s*([A-Z0-9-]{4,})", RegexOptions.IgnoreCase)] private static partial Regex ReceiptId();
    [GeneratedRegex(@"^(select|selecione|choose|escolha)\b|^-+$|^\.\.\.$", RegexOptions.IgnoreCase)] private static partial Regex Placeholder();
    [GeneratedRegex(@"^(yes|sim|y|s|true|verdadeiro|si)$")] protected static partial Regex Yes();
    // Privacy policy, personal data processing, terms and the truth of what was sent: what your authorization may accept for you.
    [GeneratedRegex(@"privacy|privacidade|personal data|dados pessoais|data processing|processing of (my )?(personal )?data|tratamento d[eo]s? dados|\bterms\b|\btermos\b|conditions|condi[cç][oõ]es|consent|consinto|consentimento|\blgpd\b|\bgdpr\b|information (i (have )?provided|provided)?\s*(is|are) (true|accurate|correct|complete)|informa[cç][oõ]es (prestadas |fornecidas )?s[aã]o (verdadeiras|ver[ií]dicas)|retain my (data|information)|armazen", RegexOptions.IgnoreCase)]
    protected static partial Regex Consent();
    [GeneratedRegex(@"newsletter|marketing|promo|job alerts?|future (openings|jobs|opportunities|positions|roles)|novidades|futuras (vagas|oportunidades)|talent (pool|community|network)|banco de talentos|outras vagas|other (openings|opportunities)", RegexOptions.IgnoreCase)]
    protected static partial Regex Marketing();
    // "pelo menos 2 anos com C#", "no mínimo 3 anos de experiência com .NET", "at least 5 years of experience with React", "2+ years in Go".
    [GeneratedRegex(@"(?:at least|minimum of|min\.?|pelo menos|no m[ií]nimo|m[ií]nimo de|mais de|more than)?\s*(?<n>\d+)\s*\+?\s*(?:or more\s+)?(?:years?|anos)\b(?:\s+(?:of|de)\s+(?:professional\s+|hands-on\s+|pr[aá]tica\s+)?(?:experience|experi[eê]ncia)(?:\s+pr[aá]tica)?)?\s+(?:with|in|using|com|em|de)\s+(?<tech>[\w#.+]+(?:\s[\w#.+]+)?)", RegexOptions.IgnoreCase)]
    internal static partial Regex MinYears();
    [GeneratedRegex(@"^(and|e|or|ou|in|em|on|no|na|for|para|including|incluindo|development|desenvolvimento|programming|programa[cç][aã]o|language|linguagem|experience|experi[eê]ncia)$", RegexOptions.IgnoreCase)]
    private static partial Regex Loose();
    [GeneratedRegex(@"\b(current|present)\s+(gross\s+|monthly\s+|annual\s+)?(sal[aá]r|compensation|pay)|sal[aá]rio atual|remunera[cç][aã]o atual|quanto (você )?ganha", RegexOptions.IgnoreCase)]
    private static partial Regex CurrentPay();
    [GeneratedRegex(@"\b(voc[eê]|anos|experi[eê]ncia|tem|possui|qual)\b", RegexOptions.IgnoreCase)] private static partial Regex Portuguese();
    [GeneratedRegex(@"/(\d+)(?:/[a-z]+)?/?$")] private static partial Regex JobId();
}

/// <summary>Formulário no padrão Greenhouse (nome e sobrenome separados).</summary>
internal sealed partial class GreenhouseLikeForm(BrowserPool browsers, IHttpClientFactory http, GuardKey key, TimeProvider time, IOptions<AutomationOptions> options)
    : LabelForm(browsers, http, key, time, options)
{
    public override string Id => "greenhouse-form";
    public override string Name => "Formulário Greenhouse";
    protected override string[] RealDomains => ["boards.greenhouse.io", "job-boards.greenhouse.io"];
    protected override Regex FakePath => Path();
    [GeneratedRegex(@"^/jobs/\d+/apply")] private static partial Regex Path();
}

/// <summary>Formulário do Lever (jobs.lever.co): nome completo num campo só, upload do currículo e perguntas que abrem outras.</summary>
internal sealed partial class LeverForm(BrowserPool browsers, IHttpClientFactory http, GuardKey key, TimeProvider time, IOptions<AutomationOptions> options)
    : LabelForm(browsers, http, key, time, options)
{
    public override string Id => "lever-form";
    public override string Name => "Formulário Lever";
    protected override string[] RealDomains => ["jobs.lever.co", "jobs.eu.lever.co"];
    protected override Regex FakePath => Path();
    [GeneratedRegex(@"^/lever/[^/]+/\d+/apply")] private static partial Regex Path();
}

/// <summary>Formulário do Ashby (jobs.ashbyhq.com): nome completo e campos extras (localização, pretensão, como soube).</summary>
internal sealed partial class AshbyForm(BrowserPool browsers, IHttpClientFactory http, GuardKey key, TimeProvider time, IOptions<AutomationOptions> options)
    : LabelForm(browsers, http, key, time, options)
{
    public override string Id => "ashby-form";
    public override string Name => "Formulário Ashby";
    protected override string[] RealDomains => ["jobs.ashbyhq.com"];
    protected override Regex FakePath => Path();
    [GeneratedRegex(@"^/ashby/[^/]+/\d+/application")] private static partial Regex Path();
}
