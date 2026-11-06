using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Base dos formulários de candidatura preenchidos por rótulo (getByLabel), nunca por seletor frágil: o id do campo pode mudar a cada
/// carga que o rótulo continua o mesmo. Prepara sem clicar em enviar; envia só com token válido e payload igual ao aprovado; reconcilia
/// só lendo. Campo que aparece depois de uma resposta (pergunta condicional) entra numa segunda passada. CAPTCHA, login, MFA e
/// declaração obrigatória (termos, certificação) param para a pessoa: o robô nunca marca declaração por ninguém.
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
    protected AutomationOptions Options => options.Value;

    public double Fit(Uri url) =>
        RealDomains.Any(d => d.Equals(url.Host, StringComparison.OrdinalIgnoreCase)) ? 1
        : options.Value.FakeAtsHosts.Contains(url.Authority, StringComparer.OrdinalIgnoreCase) && FakePath.IsMatch(url.AbsolutePath) ? 1 : 0;

    /// <summary>Pergunta conhecida: o padrão do rótulo e a chave da resposta aprovada.</summary>
    // Chaves do banco de respostas (Profile.Contracts.AnswerKeys). Anos com tecnologia: years_experience:{tecnologia}.
    protected static readonly (Regex Label, string Answer)[] Questions =
    [
        (new Regex(@"authori[sz]ed to work|autoriza", RegexOptions.IgnoreCase), "work_authorization"),
        (new Regex(@"sponsorship|visto|patroc", RegexOptions.IgnoreCase), "needs_sponsorship"),
        (new Regex(@"salary|pretens|compensation|remunera", RegexOptions.IgnoreCase), "salary_expectation"),
        (new Regex(@"notice period|aviso pr|start date|quando (você )?pode(ria)? começar", RegexOptions.IgnoreCase), "start_date"),
        (new Regex(@"english|ingl", RegexOptions.IgnoreCase), "english_level"),
        (new Regex(@"hear about|soube da vaga", RegexOptions.IgnoreCase), "how_heard"),
        (new Regex(@"relocat|mudan[cç]a", RegexOptions.IgnoreCase), "relocation"),
        (new Regex(@"(which|what) city|current location|where are you (currently )?based|based in|cidade|onde (você )?mora|localiza|^address$|^endere[cç]o", RegexOptions.IgnoreCase), "location"),
        (new Regex(@"^headline$|^t[ií]tulo profissional", RegexOptions.IgnoreCase), "headline"),
        (new Regex(@"^summary$|^resumo( profissional)?$|^about you$|^sobre voc[eê]", RegexOptions.IgnoreCase), "summary"),
    ];

    private static readonly Regex YearsWith = new(@"(?:years|anos)\b.*?\b(?:with|in|de|com|em)\s+([\w#.+]+(?:\s[\w#.+]+)?)", RegexOptions.IgnoreCase);

    public async Task<AdapterResult<PreparedForm>> PrepareAsync(ApplyTarget target, MaterialBundle materials, CancellationToken ct)
    {
        await using var lease = await browsers.LeaseAsync(Hosts(target), ct);
        var page = await lease.Context.NewPageAsync();
        var filled = await FillAsync(page, target, materials);
        return filled.Kind == AdapterResultKind.Completed
            ? AdapterResult<PreparedForm>.Ok(filled.Value!, "Formulário preenchido e conferido. Nada foi enviado.")
            : AdapterResult<PreparedForm>.Fail(filled.Kind, filled.ReasonCode!, filled.Detail);
    }

    public async Task<AdapterResult<SubmissionReceipt>> SubmitAsync(ApplyTarget target, MaterialBundle materials, SubmitToken token, CancellationToken ct)
    {
        if (token.ApplicationId != target.ApplicationId || !token.IsValid(key.Bytes, time.GetUtcNow().UtcDateTime))
            return AdapterResult<SubmissionReceipt>.Fail(AdapterResultKind.BlockedByPolicy, "invalid-token", "Autorização de envio inválida ou vencida.");
        await using var lease = await browsers.LeaseAsync(Hosts(target), ct);
        var page = await lease.Context.NewPageAsync();
        var filled = await FillAsync(page, target, materials);
        if (filled.Kind != AdapterResultKind.Completed)
            return AdapterResult<SubmissionReceipt>.Fail(filled.Kind, filled.ReasonCode!, filled.Detail);
        if (filled.Value!.PayloadHash != token.PayloadHash)
            return AdapterResult<SubmissionReceipt>.Fail(AdapterResultKind.BlockedByPolicy, "payload-changed", "O formulário mudou desde a aprovação. Nada foi enviado.");

        Chaos.Point("before-click");
        try
        {
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { NameRegex = SubmitButton() }).First.ClickAsync();
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // O clique pode ter saído antes da falha: só a reconciliação decide.
            return AdapterResult<SubmissionReceipt>.Fail(AdapterResultKind.SubmissionUnknown, "click-uncertain", "O clique de envio falhou no meio; vai para reconciliação.");
        }
        Chaos.Point("after-click");

        try
        {
            await page.GetByText(Confirmation()).Or(page.GetByText(AlreadyApplied())).Or(page.Locator("[role=alert]")).First
                .WaitForAsync(new LocatorWaitForOptions { Timeout = browsers.TimeoutMs });
        }
        catch (TimeoutException)
        {
            return AdapterResult<SubmissionReceipt>.Fail(AdapterResultKind.SubmissionUnknown, "no-confirmation", "Clicou em enviar, mas a confirmação não apareceu a tempo.");
        }

        var body = await page.Locator("body").InnerTextAsync();
        if (AlreadyApplied().IsMatch(body))
            return AdapterResult<SubmissionReceipt>.Fail(AdapterResultKind.PermanentFailure, "already-applied", "O ATS recusou: já existe candidatura com este e-mail.");
        if (!Confirmation().IsMatch(body))
            return AdapterResult<SubmissionReceipt>.Fail(AdapterResultKind.PermanentFailure, "ats-validation", "O ATS recusou o formulário e mostrou erro de validação.");
        var receipt = ReceiptId().Match(body);
        return receipt.Success
            ? AdapterResult<SubmissionReceipt>.Ok(new SubmissionReceipt(receipt.Groups[1].Value, 4, "Página de confirmação com número do recibo."))
            : AdapterResult<SubmissionReceipt>.Ok(new SubmissionReceipt("", 3, "Página de confirmação reconhecida, sem número de recibo."));
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
        if (await page.Locator("form input[type=checkbox][required]").CountAsync() > 0)
            return AdapterResult<PreparedForm>.Fail(AdapterResultKind.NeedsHuman, "declaration", "O formulário pede uma declaração obrigatória (termos ou certificação). Só você marca.");

        var fields = new List<FieldValue>();
        var done = new HashSet<string>();
        // Pergunta obrigatória sem resposta não para na primeira: o ensaio mostra todas de uma vez, para você responder numa ida só.
        var missing = new List<string>();
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
                var (value, source) = Resolve(text, type, m);
                if (value is null)
                {
                    if (required) missing.Add(text);
                    continue;
                }
                if (type == "file")
                {
                    await control.SetInputFilesAsync(value);
                    fields.Add(new FieldValue(text, $"{Path.GetFileName(value)} (sha256 {FileHash(value)[..12]})", source));
                }
                else if (tag == "select")
                {
                    var options = await control.Locator("option").AllInnerTextsAsync();
                    var match = options.FirstOrDefault(o => o.Trim().Equals(value, StringComparison.OrdinalIgnoreCase));
                    if (match is null) return AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "unsupported-option", $"A resposta aprovada não existe nas opções de \"{text}\".");
                    await control.SelectOptionAsync(new SelectOptionValue { Label = match.Trim() });
                    fields.Add(new FieldValue(text, match.Trim(), source));
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
        if (fields.Count == 0) return AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "no-fields", "Nenhum campo com rótulo foi reconhecido.");
        var hash = PayloadHash(fields);
        return AdapterResult<PreparedForm>.Ok(new PreparedForm(Id, Version, new Uri(target.ApplyUrl).Authority, hash, fields));
    }

    /// <summary>As perguntas obrigatórias sem resposta, todas no detalhe, entre aspas: a tela do ensaio abre um campo para cada uma.</summary>
    protected static AdapterResult<PreparedForm> Missing(IReadOnlyList<string> questions) => AdapterResult<PreparedForm>.Fail(AdapterResultKind.Unsupported, "unsupported-field",
        $"{(questions.Count == 1 ? "Pergunta obrigatória sem resposta aprovada" : "Perguntas obrigatórias sem resposta aprovada")}: {string.Join(" | ", questions.Select(q => $"\"{q}\""))}.");

    protected static (string? Value, string Source) Resolve(string label, string type, MaterialBundle m)
    {
        if (type == "file") return Resume().IsMatch(label) ? (m.ResumePdfPath, "currículo") : (null, "");
        if (FullName().IsMatch(label)) return ($"{m.FirstName} {m.LastName}".Trim(), "perfil");
        if (FirstName().IsMatch(label)) return (m.FirstName, "perfil");
        if (LastName().IsMatch(label)) return (m.LastName, "perfil");
        if (Email().IsMatch(label)) return (m.Email, "perfil");
        if (Phone().IsMatch(label)) return (m.Phone, "perfil");
        if (LinkedIn().IsMatch(label)) return (m.LinkedIn, "perfil");
        if (YearsWith.Match(label) is { Success: true } years)
        {
            var tech = years.Groups[1].Value.TrimEnd('?', '.', ' ');
            var key = m.Answers.Keys.FirstOrDefault(k => k.StartsWith("years_experience:", StringComparison.Ordinal)
                && (string.Equals(k[17..], tech, StringComparison.OrdinalIgnoreCase) || tech.StartsWith(k[17..], StringComparison.OrdinalIgnoreCase)));
            return key is not null ? (m.Answers[key], "anos pelas datas das experiências") : (null, "");
        }
        if (m.Answers.TryGetValue(CustomAnswer.Key(label), out var mine)) return (mine, "sua resposta");
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
    [GeneratedRegex(@"thank(s| you) for applying|application (was )?(received|submitted)|candidatura recebida", RegexOptions.IgnoreCase)] private static partial Regex Confirmation();
    [GeneratedRegex(@"already (applied|submitted)|j[aá] se candidatou", RegexOptions.IgnoreCase)] private static partial Regex AlreadyApplied();
    [GeneratedRegex(@"verification code|c[oó]digo de verifica[cç][aã]o|two-factor|autentica[cç][aã]o em dois", RegexOptions.IgnoreCase)] protected static partial Regex Mfa();
    [GeneratedRegex(@"application id:?\s*([A-Z0-9-]{4,})", RegexOptions.IgnoreCase)] private static partial Regex ReceiptId();
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
