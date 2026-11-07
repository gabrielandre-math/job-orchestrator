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

/// <summary>Vaga na fila que publica um e-mail para candidatura: o único destino que o módulo de e-mail aceita.</summary>
public sealed record EmailTarget
{
    public Guid Id { get; init; }
    public string Company { get; init; } = "";
    public string Title { get; init; } = "";
    public string Location { get; init; } = "";
    public string ApplyEmail { get; init; } = "";
    public string ApplyUrl { get; init; } = "";
    public string? CompanyDomain { get; init; }
    public int Score { get; init; }
    public DateTime FirstSeenAt { get; init; }
    /// <summary>Quem mora no Brasil pode se candidatar? (BR_ELIGIBLE_CONFIRMED, BR_ELIGIBLE_POSSIBLE, ...)</summary>
    public string? Eligibility { get; init; }
}

/// <summary>Vaga da fila com o caminho de candidatura: o ensaio dos formulários usa as que têm formulário de ATS.</summary>
public sealed record QueuedJob
{
    public Guid Id { get; init; }
    public string Company { get; init; } = "";
    public string Title { get; init; } = "";
    public string Location { get; init; } = "";
    public string ApplyUrl { get; init; } = "";
    public string ApplyChannel { get; init; } = "";
    public string? Eligibility { get; init; }
    public int Score { get; init; }
}

/// <summary>O automático só envia sozinho para vaga que confirma aceitar quem mora no Brasil (ou paga visto e mudança); a remota "a confirmar" espera você.</summary>
public static class AutoEligibility
{
    public static bool Allows(string? eligibility) => eligibility is "BR_ELIGIBLE_CONFIRMED" or "RELOCATION_WITH_SPONSORSHIP";
}

/// <summary>Quantas vagas abertas e únicas têm este texto de local, e quantas aceitam quem mora no Brasil (confirmado, possível, restrita).</summary>
public sealed record LocationCount
{
    public string Location { get; init; } = "";
    public int Open { get; init; }
    public int BrazilOk { get; init; }
    public int BrazilPossible { get; init; }
    public int Restricted { get; init; }
}

/// <summary>Os números das vagas abertas, separados como a especificação V2 pede: únicas, empresas, e elegibilidade de quem mora no Brasil.</summary>
public sealed record OpenSummary
{
    public int Open { get; init; }
    public int Companies { get; init; }
    public int BrazilOk { get; init; }
    public int BrazilPossible { get; init; }
    public int Restricted { get; init; }
    public int Review { get; init; }
    public int Unknown { get; init; }
    public int WithDate { get; init; }
}

/// <summary>Uma conferência do histórico importado com as fontes ao vivo: quantos quadros, quantas vagas abriram, fecharam ou ficaram sem resposta.</summary>
public sealed record HistoryCheck(int Boards, int Checked, int Open, int Closed, int Failed);

/// <summary>Quadro do histórico com vaga aberta conferida ao vivo e que quem mora no Brasil pode disputar: pista para o Explorador.</summary>
public sealed record HistoryLead(string Source, int Open);

/// <summary>Published by Mail: the application e-mail of this job left your account and the mail server took it (not proof of delivery).</summary>
public sealed record JobEmailSentV1(Guid JobId, string To, string MessageId, DateTime SentAt, string Kind)
{
    public const string EventType = "mail.job-email-sent.v1";
}

/// <summary>Published by Mail: the company answered the e-mail of this job. Class is what the local model read (interview_invite, offer, rejection...).</summary>
public sealed record JobEmailRepliedV1(Guid JobId, string? Class, DateTime At)
{
    public const string EventType = "mail.job-email-replied.v1";
}

/// <summary>Contrato público do módulo Jobs.</summary>
public interface IJobsApi
{
    /// <summary>False while the campaign only searches or only prepares: nothing leaves on its own, e-mails included.</summary>
    Task<bool> SendingAllowedAsync(CancellationToken ct);
    Task<JobCounts> CountsAsync(CancellationToken ct);
    Task<IReadOnlyList<SourceCount>> SourcesAsync(CancellationToken ct);
    Task<JobText?> TextAsync(Guid jobId, CancellationToken ct);
    Task<IReadOnlyList<EmailTarget>> EmailTargetsAsync(CancellationToken ct);
    Task<IReadOnlyList<QueuedJob>> QueueAsync(string channel, int limit, CancellationToken ct);
    Task<string?> EligibilityAsync(Guid jobId, CancellationToken ct);
    /// <summary>Vagas abertas e únicas agrupadas pelo texto do local, com a elegibilidade de quem mora no Brasil.</summary>
    Task<IReadOnlyList<LocationCount>> OpenLocationsAsync(CancellationToken ct);
    Task<OpenSummary> OpenSummaryAsync(CancellationToken ct);
    /// <summary>Links de candidatura e domínios de empresa das vagas recentes: onde o Explorador garimpa quadros de ATS e sites de carreira.</summary>
    Task<IReadOnlyList<string>> ApplyLinksAsync(int limit, CancellationToken ct);
    Task<IReadOnlyList<string>> CompanyDomainsAsync(int limit, CancellationToken ct);
    /// <summary>Confere até N quadros do histórico importado com a fonte ao vivo (portão de permissão incluído): abre só o que o quadro lista hoje.</summary>
    Task<HistoryCheck> RevalidateHistoryAsync(int boards, CancellationToken ct);
    Task<IReadOnlyList<HistoryLead>> HistoryLeadsAsync(int limit, CancellationToken ct);
}
