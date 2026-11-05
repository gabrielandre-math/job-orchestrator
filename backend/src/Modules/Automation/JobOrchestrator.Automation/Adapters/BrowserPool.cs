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

    public int TimeoutMs => _o.TimeoutSeconds * 1000;

    public async Task<BrowserLease> LeaseAsync(IReadOnlyCollection<string> allowedHosts, CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        try
        {
            var browser = await BrowserAsync();
            var context = await browser.NewContextAsync(new BrowserNewContextOptions { AcceptDownloads = false, Locale = "en-US" });
            context.SetDefaultTimeout(TimeoutMs);
            await context.RouteAsync("**/*", async route =>
            {
                if (Allowed(route.Request.Url, allowedHosts)) await route.ContinueAsync();
                else await route.AbortAsync("blockedbyclient");
            });
            return new BrowserLease(context, _slots);
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }

    /// <summary>Só http(s), só hosts da lista. Rede privada só quando o host está explicitamente na lista (ATS de teste).</summary>
    internal static bool Allowed(string url, IReadOnlyCollection<string> allowedHosts)
    {
        if (url.StartsWith("data:", StringComparison.Ordinal) || url == "about:blank") return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return false;
        var authority = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        if (allowedHosts.Contains(authority, StringComparer.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(uri.Host, out var ip) && (IPAddress.IsLoopback(ip) || IsPrivate(ip))) return false;
        return allowedHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPrivate(IPAddress ip)
    {
        var b = ip.MapToIPv4().GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    private async Task<IBrowser> BrowserAsync()
    {
        if (_browser is { IsConnected: true }) return _browser;
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
            return _browser;
        }
        finally
        {
            _launch.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }
}

/// <summary>Um contexto emprestado. Ao devolver, o contexto fecha e a vaga volta ao pool.</summary>
internal sealed class BrowserLease(IBrowserContext context, SemaphoreSlim slots) : IAsyncDisposable
{
    public IBrowserContext Context => context;

    public async ValueTask DisposeAsync()
    {
        try { await context.CloseAsync(); }
        finally { slots.Release(); }
    }
}
