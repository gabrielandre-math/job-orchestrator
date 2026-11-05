namespace JobOrchestrator.Automation.Adapters;

/// <summary>O que todo adapter declara: identidade, versão, capacidades e onde atua.</summary>
internal interface IAdapter
{
    string Id { get; }
    string Version { get; }
    string Name { get; }
    string Kind { get; }
    /// <summary>Risco de 0 a 1: navegador pesa mais que API pública.</summary>
    double Risk { get; }
    IReadOnlyList<Capability> Capabilities { get; }
    IReadOnlyList<string> Domains { get; }
    /// <summary>1 quando o domínio é exatamente o do adapter; 0,5 quando é genérico; 0 quando não atende.</summary>
    double Fit(Uri url);
}

/// <summary>Fonte de vagas (capability DiscoverJobs e CheckPostingOpen).</summary>
internal interface ISourceConnector : IAdapter
{
    bool Accepts(string source);
    Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct);
    Task<PostingStatus> CheckOpenAsync(string applyUrl, CancellationToken ct);
}

/// <summary>Formulário de candidatura: prepara sem enviar, envia só com token, reconcilia só lendo.</summary>
internal interface IFormAdapter : IAdapter
{
    Task<AdapterResult<PreparedForm>> PrepareAsync(ApplyTarget target, MaterialBundle materials, CancellationToken ct);
    Task<AdapterResult<SubmissionReceipt>> SubmitAsync(ApplyTarget target, MaterialBundle materials, SubmitToken token, CancellationToken ct);
    Task<AdapterResult<ReconcileResult>> ReconcileAsync(ApplyTarget target, MaterialBundle materials, CancellationToken ct);
}

/// <summary>Resultado tipado de uma operação de adapter.</summary>
internal sealed record AdapterResult<T>(AdapterResultKind Kind, T? Value, string? ReasonCode, string Detail)
{
    public static AdapterResult<T> Ok(T value, string detail = "") => new(AdapterResultKind.Completed, value, null, detail);
    public static AdapterResult<T> Fail(AdapterResultKind kind, string code, string detail) => new(kind, default, code, detail);
}

/// <summary>Opções da automação, lidas de "Automation" no appsettings.</summary>
internal sealed class AutomationOptions
{
    /// <summary>Hosts do ATS de teste tratados como Greenhouse (ex.: 127.0.0.1:5470).</summary>
    public string[] FakeAtsHosts { get; set; } = [];
    public BrowserOptions Browser { get; set; } = new();
    public RouterWeights Weights { get; set; } = new();
}

internal sealed class BrowserOptions
{
    public string? ExecutablePath { get; set; } = File.Exists("/usr/bin/chromium") ? "/usr/bin/chromium" : null;
    public bool Headless { get; set; } = true;
    public int MaxContexts { get; set; } = 2;
    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>Pesos da fórmula do roteador (docs/PLANO.md §5).</summary>
internal sealed class RouterWeights
{
    public double Success { get; set; } = 0.35;
    public double Compat { get; set; } = 0.15;
    public double Risk { get; set; } = 0.15;
    public double Health { get; set; } = 0.15;
    public double Latency { get; set; } = 0.10;
    public double Quota { get; set; } = 0.10;
}
