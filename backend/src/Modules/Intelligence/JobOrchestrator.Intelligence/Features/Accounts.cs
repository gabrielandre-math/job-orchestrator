using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using JobOrchestrator.Intelligence.Providers;

namespace JobOrchestrator.Intelligence.Features;

/// <summary>Uma conta como a tela mostra.</summary>
internal sealed record AccountView(string Id, string Kind, string Name, int Position, bool Enabled, bool OwnFolder, string? BaseUrl, Dictionary<string, string> Models, long? MonthlyTokens, long TokensThisMonth);

/// <summary>Pedido para conectar uma conta nova.</summary>
internal sealed record NewAccount(string Kind, string Name, string? ApiKey, string? BaseUrl, long? MonthlyTokens, Dictionary<string, string>? Models);

/// <summary>Mudança numa conta: nome, ligada, teto, modelos por degrau.</summary>
internal sealed record AccountPatch(string? Name, bool? Enabled, long? MonthlyTokens, Dictionary<string, string>? Models);

/// <summary>Login pela assinatura em andamento: o link (se o navegador não abrir sozinho) e o fim do processo.</summary>
internal sealed record LoginView(bool Running, string? Url, bool? Ok, string? Output);

/// <summary>
/// Contas de IA: várias por provedor (dois Claudes, dois ChatGPTs), assinatura pelo login do CLI no navegador ou chave de API
/// no gnome-keyring. A ordem da lista é a ordem de gasto. Remover uma conta apaga a chave do keyring e a pasta de login própria.
/// </summary>
internal sealed partial class AccountsApi(IntelligenceDbContext db, IHttpClientFactory http, TimeProvider time)
{
    private static readonly string[] Kinds = ["claude-cli", "codex-cli", "anthropic-api", .. ApiDefaults.Compatible];
    private static readonly ConcurrentDictionary<string, LoginJob> Logins = new();

    public async Task<IReadOnlyList<AccountView>> ListAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var used = await db.Calls.Where(c => c.At >= month).GroupBy(c => c.ProviderId).Select(g => new { g.Key, Tokens = g.Sum(c => (long)c.Tokens) }).ToDictionaryAsync(x => x.Key, x => x.Tokens, ct);
        return [.. (await db.Accounts.AsNoTracking().OrderBy(a => a.Position).ToListAsync(ct)).Select(a => new AccountView(
            a.Id, a.Kind, a.Name, a.Position, a.Enabled, Owned(a.ConfigDir), a.BaseUrl, a.ModelMap, a.MonthlyTokens, used.GetValueOrDefault(a.Id)))];
    }

    public async Task<Result<AccountView>> CreateAsync(NewAccount n, CancellationToken ct)
    {
        if (!Kinds.Contains(n.Kind)) return Error.Validation("kind", "Tipo de conta desconhecido.");
        var name = n.Name.Trim();
        if (name.Length is 0 or > 80) return Error.Validation("name", "Dê um nome de até 80 letras para a conta.");
        var api = n.Kind != "claude-cli" && n.Kind != "codex-cli";
        if (api && string.IsNullOrWhiteSpace(n.ApiKey)) return Error.Validation("api-key", "Cole a chave de API.");
        if (api && n.MonthlyTokens is null or <= 0) return Error.Validation("monthly-tokens", "Chave de API cobra por uso: defina o teto de tokens por mês.");
        if (n.Kind == "openai-compatible" && !Uri.TryCreate(n.BaseUrl, UriKind.Absolute, out _)) return Error.Validation("base-url", "Informe o endereço da API (ex.: https://api.exemplo.com/v1).");

        var id = Slug(name);
        for (var i = 2; await db.Accounts.AnyAsync(a => a.Id == id, ct); i++) id = $"{Slug(name)}-{i}";
        var account = new Account
        {
            Id = id, Kind = n.Kind, Name = name, Position = (await db.Accounts.MaxAsync(a => (int?)a.Position, ct) ?? 0) + 1, Enabled = true,
            BaseUrl = n.Kind == "openai-compatible" ? n.BaseUrl!.Trim().TrimEnd('/') : null, MonthlyTokens = api ? n.MonthlyTokens : null,
            Models = JsonSerializer.Serialize(n.Models ?? []), CreatedAt = time.GetUtcNow().UtcDateTime
        };
        if (!api)
        {
            // Assinatura: pasta própria, então dá para ter quantas contas quiser do mesmo provedor sem misturar login.
            account.ConfigDir = Path.Combine(ProviderCatalog.AccountsRoot, id);
            Directory.CreateDirectory(account.ConfigDir);
        }
        else if (!await Keyring.StoreAsync(id, n.ApiKey!.Trim(), ct))
            return Error.Validation("keyring", "Não foi possível guardar a chave no gnome-keyring. Confira se o keyring está destravado.");
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        if (api && (n.Models is null || n.Models.Count == 0))
        {
            // A lista de modelos confirma que a chave funciona e já escolhe o leve e o principal.
            var models = await ApiProbe.ModelsAsync(account, http, ct);
            if (models.Count == 0)
            {
                db.Accounts.Remove(account);
                await db.SaveChangesAsync(ct);
                await Keyring.ClearAsync(id, ct);
                return Error.Validation("key-refused", "O provedor não aceitou a chave (ou não listou nenhum modelo). Confira se copiou a chave inteira.");
            }
            account.Models = JsonSerializer.Serialize(ApiDefaults.Pick(models));
            await db.SaveChangesAsync(ct);
        }
        ProviderCatalog.Invalidate();
        return (await ListAsync(ct)).First(a => a.Id == id);
    }

    public async Task<Result<Done>> UpdateAsync(string id, AccountPatch p, CancellationToken ct)
    {
        var a = await db.Accounts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Error.NotFound("account-not-found", "Conta não encontrada.");
        if (p.Name is { } name && name.Trim().Length is > 0 and <= 80) a.Name = name.Trim();
        if (p.Enabled is { } on) a.Enabled = on;
        if (p.MonthlyTokens is { } cap && a.Paid) a.MonthlyTokens = cap > 0 ? cap : null;
        if (p.Models is { } models) a.Models = JsonSerializer.Serialize(models.Where(m => !string.IsNullOrWhiteSpace(m.Value)).ToDictionary(m => m.Key, m => m.Value.Trim()));
        await db.SaveChangesAsync(ct);
        ProviderCatalog.Invalidate();
        return new Done();
    }

    public async Task<Result<Done>> ReorderAsync(string[] ids, CancellationToken ct)
    {
        var accounts = await db.Accounts.ToListAsync(ct);
        for (var i = 0; i < ids.Length; i++)
            if (accounts.FirstOrDefault(a => a.Id == ids[i]) is { } a) a.Position = i + 1;
        await db.SaveChangesAsync(ct);
        ProviderCatalog.Invalidate();
        return new Done();
    }

    public async Task<Result<Done>> DeleteAsync(string id, CancellationToken ct)
    {
        var a = await db.Accounts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Error.NotFound("account-not-found", "Conta não encontrada.");
        if (a.Paid) await Keyring.ClearAsync(a.Id, ct);
        // Só apaga pasta de login criada aqui; ~/.claude e ~/.codex são do seu uso no terminal e nunca são tocadas.
        if (Owned(a.ConfigDir) && Directory.Exists(a.ConfigDir)) Directory.Delete(a.ConfigDir!, recursive: true);
        db.Accounts.Remove(a);
        await db.SaveChangesAsync(ct);
        ProviderCatalog.Invalidate();
        return new Done();
    }

    public async Task<IReadOnlyList<string>> ModelsAsync(string id, CancellationToken ct) =>
        await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) is { Paid: true } a ? await ApiProbe.ModelsAsync(a, http, ct) : [];

    /// <summary>
    /// Login pela assinatura, como os apps grandes fazem: o CLI oficial abre o navegador, você entra na sua conta e volta.
    /// Só para contas com pasta própria: o login do Claude Code e do Codex que você usa no terminal fica como está.
    /// </summary>
    public async Task<Result<LoginView>> LoginAsync(string id, CancellationToken ct)
    {
        var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Error.NotFound("account-not-found", "Conta não encontrada.");
        if (a.Kind is not ("claude-cli" or "codex-cli")) return Error.Validation("not-subscription", "Esta conta usa chave de API, não login.");
        if (!Owned(a.ConfigDir)) return Error.Forbidden("shared-login", "Esta conta usa o login do seu terminal. Entre por lá para não derrubar a sua sessão.");
        if (Logins.TryGetValue(id, out var running) && running.Running) return running.View();
        var job = LoginJob.Start(a);
        Logins[id] = job;
        await job.WaitForUrlAsync(TimeSpan.FromSeconds(8), ct);
        ProviderCatalog.Invalidate();
        return job.View();
    }

    public LoginView LoginStatus(string id) => Logins.TryGetValue(id, out var j) ? j.View() : new LoginView(false, null, null, null);

    private static bool Owned(string? dir) =>
        dir is { Length: > 0 } && Path.GetFullPath(dir).StartsWith(Path.GetFullPath(ProviderCatalog.AccountsRoot) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static string Slug(string name)
    {
        var plain = new string([.. name.Normalize(System.Text.NormalizationForm.FormD).Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)]).ToLowerInvariant();
        var s = NonWord().Replace(plain, "-").Trim('-');
        return s.Length == 0 ? "conta" : s.Length > 36 ? s[..36] : s;
    }

    [GeneratedRegex(@"[^a-z0-9]+")] private static partial Regex NonWord();
}

/// <summary>Um processo de login de CLI rodando em segundo plano, com o link que ele imprimiu.</summary>
internal sealed partial class LoginJob
{
    private readonly Process _process;
    private readonly System.Text.StringBuilder _out = new();
    private readonly TaskCompletionSource _url = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? Url { get; private set; }
    public bool Running => !_process.HasExited;

    private LoginJob(Process p) => _process = p;

    public static LoginJob Start(Account a)
    {
        var claude = a.Kind == "claude-cli";
        var info = new ProcessStartInfo(CliRunner.Find(claude ? "claude" : "codex") ?? (claude ? "claude" : "codex"))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, WorkingDirectory = a.ConfigDir!
        };
        foreach (var arg in claude ? new[] { "auth", "login", "--claudeai" } : ["login"]) info.ArgumentList.Add(arg);
        info.Environment.Clear();
        // Ambiente limpo mais o necessário para abrir o navegador da sessão.
        foreach (var key in new[] { "PATH", "HOME", "USER", "LANG", "TERM", "XDG_RUNTIME_DIR", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "DBUS_SESSION_BUS_ADDRESS", "DISPLAY", "WAYLAND_DISPLAY", "BROWSER" })
            if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } v) info.Environment[key] = v;
        info.Environment[claude ? "CLAUDE_CONFIG_DIR" : "CODEX_HOME"] = a.ConfigDir!;
        var p = new Process { StartInfo = info, EnableRaisingEvents = true };
        var job = new LoginJob(p);
        void Line(string? line)
        {
            if (line is null) return;
            lock (job._out) { if (job._out.Length < 8000) job._out.AppendLine(line); }
            if (job.Url is null && Link().Match(line) is { Success: true } m) { job.Url = m.Value.TrimEnd('.', ')'); job._url.TrySetResult(); }
        }
        p.OutputDataReceived += (_, e) => Line(e.Data);
        p.ErrorDataReceived += (_, e) => Line(e.Data);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        // Dez minutos para você terminar no navegador; depois o processo é encerrado.
        _ = Task.Delay(TimeSpan.FromMinutes(10)).ContinueWith(_ => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        return job;
    }

    public async Task WaitForUrlAsync(TimeSpan max, CancellationToken ct) => await Task.WhenAny(_url.Task, _process.WaitForExitAsync(ct), Task.Delay(max, ct));

    public LoginView View()
    {
        string tail;
        lock (_out) tail = _out.ToString();
        return new LoginView(Running, Url, Running ? null : _process.ExitCode == 0, tail.Length > 1200 ? tail[^1200..] : tail);
    }

    [GeneratedRegex(@"https?://[^\s""'<>]+")] private static partial Regex Link();
}

/// <summary>Rotas HTTP (Carter) das contas.</summary>
internal sealed class AccountEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/accounts").WithTags("Intelligence");
        g.MapGet("", async (AccountsApi api, CancellationToken ct) => Results.Ok(await api.ListAsync(ct)));
        g.MapPost("", async (NewAccount body, AccountsApi api, CancellationToken ct) => (await api.CreateAsync(body, ct)).ToHttp(v => Results.Created($"/api/accounts/{v.Id}", v)));
        g.MapPut("order", async (string[] ids, AccountsApi api, CancellationToken ct) => (await api.ReorderAsync(ids, ct)).ToHttp(_ => Results.NoContent()));
        g.MapPut("{id}", async (string id, AccountPatch body, AccountsApi api, CancellationToken ct) => (await api.UpdateAsync(id, body, ct)).ToHttp(_ => Results.NoContent()));
        g.MapDelete("{id}", async (string id, AccountsApi api, CancellationToken ct) => (await api.DeleteAsync(id, ct)).ToHttp(_ => Results.NoContent()));
        g.MapGet("{id}/models", async (string id, AccountsApi api, CancellationToken ct) => Results.Ok(await api.ModelsAsync(id, ct)));
        g.MapPost("{id}/login", async (string id, AccountsApi api, CancellationToken ct) => (await api.LoginAsync(id, ct)).ToHttp(Results.Ok));
        g.MapGet("{id}/login", (string id, AccountsApi api) => Results.Ok(api.LoginStatus(id)));
    }
}
