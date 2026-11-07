using System.Net;
using Microsoft.Playwright;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Navegador compartilhado com contextos isolados: cada operação ganha um contexto limpo, sem cookies de ninguém,
/// e só navega nos hosts permitidos. Nunca usa o perfil pessoal do usuário.
/// </summary>
internal sealed class BrowserPool(IOptions<AutomationOptions> options, ILogger<BrowserPool> log) : IAsyncDisposable
{
    private readonly BrowserOptions _o = options.Value.Browser;
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.Value.Browser.MaxContexts));
    private readonly SemaphoreSlim _launch = new(1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private int _active;
    private long _lastUsed;
    private Timer? _idle;
    /// <summary>
    /// Navegador parado não fica aberto (documentação do Playwright: contexto por tarefa, navegador fechado quando não há uso): parado
    /// por estes minutos, fecha com o driver e libera os ~400 MB; o próximo empréstimo abre de novo.
    /// </summary>
    private static readonly TimeSpan IdleClose = TimeSpan.FromMinutes(3);
    /// <summary>O formulário não precisa de imagem, vídeo nem fonte: não baixar poupa memória e rede no navegador.</summary>
    private static readonly HashSet<string> Heavy = ["image", "media", "font"];

    public int TimeoutMs => _o.TimeoutSeconds * 1000;

    /// <summary>
    /// navigationDomain: the page may only open pages of this site and its subdomains (the generic form, whose site has no adapter).
    /// allowedHosts "*": any public host a page needs (its API, its CDN), never the private network, this machine or an anti-robot check.
    /// </summary>
    public async Task<BrowserLease> LeaseAsync(IReadOnlyCollection<string> allowedHosts, CancellationToken ct, bool challengeResources = false, string? navigationOrigin = null, bool? headless = null,
        string? navigationDomain = null)
    {
        await _slots.WaitAsync(ct);
        Interlocked.Increment(ref _active);
        IBrowser? dedicated = null;
        IBrowserContext? context = null;
        try
        {
            // Interactive controlled tests use a separate browser, so they never change the production pool's headless setting.
            var browser = headless is { } requested && requested != _o.Headless
                ? dedicated = await (await PlaywrightAsync()).Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = requested, ExecutablePath = _o.ExecutablePath })
                : await BrowserAsync();
            context = await browser.NewContextAsync(new BrowserNewContextOptions { AcceptDownloads = false, Locale = "en-US" });
            context.SetDefaultTimeout(TimeoutMs);
            await context.RouteAsync("**/*", async route =>
            {
                if (navigationOrigin is not null && route.Request.IsNavigationRequest && route.Request.Frame.ParentFrame is null
                    && (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var navigation) || navigation.GetLeftPart(UriPartial.Authority) != navigationOrigin)) await route.AbortAsync("blockedbyclient");
                else if (navigationDomain is not null && route.Request.IsNavigationRequest && route.Request.Frame.ParentFrame is null
                    && (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var site) || !(site.Host.Equals(navigationDomain, StringComparison.OrdinalIgnoreCase)
                        || site.Host.EndsWith("." + navigationDomain, StringComparison.OrdinalIgnoreCase)))) await route.AbortAsync("blockedbyclient");
                else if (Heavy.Contains(route.Request.ResourceType) && !(challengeResources && route.Request.ResourceType == "image")) await route.AbortAsync("blockedbyclient");
                else if (Allowed(route.Request.Url, allowedHosts)) await route.ContinueAsync();
                else await route.AbortAsync("blockedbyclient");
            });
            return new BrowserLease(context, _slots, Returned, dedicated);
        }
        catch
        {
            try { if (context is not null) await context.CloseAsync(); if (dedicated is not null) await dedicated.CloseAsync(); }
            finally { Returned(); _slots.Release(); }
            throw;
        }
    }

    private async Task<IPlaywright> PlaywrightAsync()
    {
        await _launch.WaitAsync();
        try { return _playwright ??= await Playwright.CreateAsync(); }
        finally { _launch.Release(); }
    }

    /// <summary>Contexto devolvido: marca a hora para o fechamento por inatividade.</summary>
    private void Returned()
    {
        Interlocked.Exchange(ref _lastUsed, Environment.TickCount64);
        Interlocked.Decrement(ref _active);
    }

    private async Task CloseIdleAsync()
    {
        if (Volatile.Read(ref _active) > 0 || _browser is null || Environment.TickCount64 - Interlocked.Read(ref _lastUsed) < IdleClose.TotalMilliseconds) return;
        await _launch.WaitAsync();
        try
        {
            if (Volatile.Read(ref _active) > 0 || _browser is null) return;
            await _browser.CloseAsync();
            _browser = null;
            _playwright?.Dispose();
            _playwright = null;
            log.LogInformation("Browser closed after {Minutes} idle minutes", IdleClose.TotalMinutes);
        }
        catch (Exception ex) when (ex is PlaywrightException or ObjectDisposedException or InvalidOperationException) { log.LogWarning("Closing the idle browser failed: {Error}", ex.Message); _browser = null; }
        finally { _launch.Release(); }
    }

    /// <summary>Só http(s), só hosts da lista. Rede privada só quando o host está explicitamente na lista (ATS de teste).</summary>
    internal static bool Allowed(string url, IReadOnlyCollection<string> allowedHosts)
    {
        if (url.StartsWith("data:", StringComparison.Ordinal) || url == "about:blank") return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return false;
        var authority = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        if (allowedHosts.Contains(authority, StringComparer.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(uri.Host, out var ip) && (IPAddress.IsLoopback(ip) || IsPrivate(ip))) return false;
        if (allowedHosts.Contains("*"))
            return !uri.IsLoopback && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                && !AntiRobot.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase))
                && !uri.AbsolutePath.Contains("/recaptcha/", StringComparison.OrdinalIgnoreCase);
        return allowedHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Anti-robot checks are never loaded: a page that needs one stays with you.</summary>
    private static readonly string[] AntiRobot = ["recaptcha.net", "hcaptcha.com", "challenges.cloudflare.com", "arkoselabs.com", "funcaptcha.com", "geetest.com"];

    private static bool IsPrivate(IPAddress ip)
    {
        var b = ip.MapToIPv4().GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    // Sempre pela trava: o fechamento por inatividade nunca fecha um navegador que acabou de ser entregue.
    private async Task<IBrowser> BrowserAsync()
    {
        await _launch.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true }) return _browser;
            _playwright ??= await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _o.Headless,
                ExecutablePath = _o.ExecutablePath,
                Args = ["--disable-extensions", "--disable-sync", "--no-first-run"]
            });
            log.LogInformation("Browser started: {Version}", _browser.Version);
            _idle ??= new Timer(_ => _ = CloseIdleAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            return _browser;
        }
        finally
        {
            _launch.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_idle is not null) await _idle.DisposeAsync();
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }
}

/// <summary>Um contexto emprestado. Ao devolver, o contexto fecha e a vaga volta ao pool.</summary>
internal sealed class BrowserLease(IBrowserContext context, SemaphoreSlim slots, Action returned, IBrowser? dedicated = null) : IAsyncDisposable
{
    private int disposed;
    public IBrowserContext Context => context;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { try { await context.CloseAsync(); } finally { if (dedicated is not null) await dedicated.CloseAsync(); } }
        finally { returned(); slots.Release(); }
    }
}
