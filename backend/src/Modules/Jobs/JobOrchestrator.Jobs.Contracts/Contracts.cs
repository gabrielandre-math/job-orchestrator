namespace JobOrchestrator.Jobs.Contracts;

// Contratos públicos do módulo Jobs.

/// <summary>Evento de integração: a vaga passou nos filtros e na nota, e entrou na fila da campanha.</summary>
/// <summary>ApplyChannel: "ats" (formulário aberto), "manual" (site com login, vai pelo copiloto), "email" ou "link".</summary>
public sealed record JobShortlistedV1(Guid JobId, Guid CampaignId, string CanonicalKey, string Company, string Title, string Location, string ApplyUrl, int Score, string ApplyChannel = "ats")
{
    public const string EventType = "jobs.job-shortlisted.v1";
}

/// <summary>Evento de integração: um link salvo revelou um board de ATS. A campanha passa a vigiar a empresa.</summary>
public sealed record SourceDiscoveredV1(Guid CampaignId, string Source, DateTime At)
{
    public const string EventType = "jobs.source-discovered.v1";
}

/// <summary>Contagem das vagas por situação.</summary>
public sealed record JobCounts(int Total, int Discovered, int Shortlisted, int Rejected, int Duplicates, int Expired);

/// <summary>Quantas vagas cada fonte trouxe.</summary>
public sealed record SourceCount(string Source, int Postings, int Shortlisted);

/// <summary>A vaga para quem compõe materiais. A descrição é dado não confiável: entra delimitada no prompt, nunca como instrução.</summary>
public sealed record JobText
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Company { get; init; } = "";
    public string Location { get; init; } = "";
    public string Description { get; init; } = "";
    public string Source { get; init; } = "";
    public string? Seniority { get; init; }
    public string? Modality { get; init; }
    public string? Contract { get; init; }
    public string? English { get; init; }
    public string? SalaryCurrency { get; init; }
}

/// <summary>Contrato público do módulo Jobs.</summary>
public interface IJobsApi
{
    Task<JobCounts> CountsAsync(CancellationToken ct);
    Task<IReadOnlyList<SourceCount>> SourcesAsync(CancellationToken ct);
    Task<JobText?> TextAsync(Guid jobId, CancellationToken ct);
}
