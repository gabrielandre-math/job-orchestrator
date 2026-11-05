namespace JobOrchestrator.Jobs.Contracts;

// Contratos públicos do módulo Jobs.

/// <summary>Evento de integração: a vaga passou nos filtros e na nota, e entrou na fila da campanha.</summary>
public sealed record JobShortlistedV1(Guid JobId, Guid CampaignId, string CanonicalKey, string Company, string Title, string Location, string ApplyUrl, int Score)
{
    public const string EventType = "jobs.job-shortlisted.v1";
}

/// <summary>Contagem das vagas por situação.</summary>
public sealed record JobCounts(int Total, int Discovered, int Shortlisted, int Rejected, int Duplicates, int Expired);

/// <summary>Quantas vagas cada fonte trouxe.</summary>
public sealed record SourceCount(string Source, int Postings, int Shortlisted);

/// <summary>Contrato público do módulo Jobs.</summary>
public interface IJobsApi
{
    Task<JobCounts> CountsAsync(CancellationToken ct);
    Task<IReadOnlyList<SourceCount>> SourcesAsync(CancellationToken ct);
}
