using JobOrchestrator.Intelligence.Data;

namespace JobOrchestrator.Intelligence.Providers;

/// <summary>Os provedores de agora: todas as contas (ligadas ou não) e o Llama local. Enabled é a ordem de gasto.</summary>
internal sealed record ProviderSet(IReadOnlyList<IProvider> All, IReadOnlyList<string> Enabled);

/// <summary>Monta os provedores a partir das contas no banco. Fica em cache até uma conta mudar.</summary>
internal sealed class ProviderCatalog(IConfiguration config, IHttpClientFactory http, OllamaLocal ollama)
{
    private static (DateTime At, ProviderSet Set)? cache;
    private static readonly Lock Gate = new();

    public static void Invalidate() { lock (Gate) cache = null; }

    public ProviderSet Current(IntelligenceDbContext db)
    {
        lock (Gate)
        {
            if (cache is { } c && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(5)) return c.Set;
            var accounts = db.Accounts.AsNoTracking().OrderBy(a => a.Position).ToList();
            var providers = accounts.Select(Build).OfType<IProvider>().Append(ollama).ToList();
            var set = new ProviderSet(providers, [.. accounts.Where(a => a.Enabled).Select(a => a.Id)]);
            cache = (DateTime.UtcNow, set);
            return set;
        }
    }

    /// <summary>
    /// A pasta da conta como foi gravada; se ela não existe aqui (o app roda em contêiner, com outro HOME), a mesma conta na raiz de contas
    /// deste ambiente. Assim a conta conectada no host funciona no Docker sem reconectar.
    /// </summary>
    private static string? Folder(Account a) =>
        a.ConfigDir is null || Directory.Exists(a.ConfigDir) ? a.ConfigDir
        : Directory.Exists(Path.Combine(AccountsRoot, a.Id)) ? Path.Combine(AccountsRoot, a.Id) : a.ConfigDir;

    private IProvider? Build(Account a) => a.Kind switch
    {
        "claude-cli" => new ClaudeCli(a.Id, a.Name, Folder(a), config),
        "codex-cli" => new CodexCli(a.Id, a.Name, Folder(a), config),
        "anthropic-api" => new AnthropicApi(a, http),
        _ when ApiDefaults.Compatible.Contains(a.Kind) => new OpenAiCompatible(a, http),
        _ => null
    };

    /// <summary>Pasta de cada conta de assinatura extra (login próprio), fora do git e fora das pastas padrão dos CLIs.</summary>
    public static string AccountsRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "job-orchestrator", "accounts");
}
