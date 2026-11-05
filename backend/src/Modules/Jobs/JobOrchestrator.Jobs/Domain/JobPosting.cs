using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Jobs.Domain;

/// <summary>Em que ponto está uma vaga dentro da campanha.</summary>
internal enum JobStatus { Discovered, Scored, Shortlisted, Rejected, Duplicate, Expired }

/// <summary>Agregado: uma vaga encontrada por uma campanha, com identidade canônica, avaliação e histórico de abertura.</summary>
internal sealed class JobPosting : AggregateRoot
{
    private JobPosting() { }
    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public string Source { get; private set; } = "";
    public string ExternalId { get; private set; } = "";
    public string CanonicalKey { get; private set; } = "";
    public string Company { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Location { get; private set; } = "";
    public string ApplyUrl { get; private set; } = "";
    public string Description { get; private set; } = "";
    public DateTime? SourceUpdatedAt { get; private set; }
    public DateTime FirstSeenAt { get; private set; }
    public DateTime LastSeenAt { get; private set; }
    public DateTime? LastVerifiedAt { get; private set; }
    public bool Open { get; private set; } = true;
    public JobStatus Status { get; private set; }
    public int? Score { get; private set; }
    public double? Confidence { get; private set; }
    public string Breakdown { get; private set; } = "[]";
    public string? Reason { get; private set; }
    public Guid? DuplicateOf { get; private set; }
    public string? Seniority { get; private set; }
    public string? Modality { get; private set; }
    public string? SalaryText { get; private set; }
    public decimal? SalaryMin { get; private set; }
    public decimal? SalaryMax { get; private set; }
    public string? SalaryCurrency { get; private set; }
    public string? Contract { get; private set; }
    public string? English { get; private set; }
    public string? Sponsorship { get; private set; }
    public string ApplyChannel { get; private set; } = "ats";
    public string? ApplyEmail { get; private set; }
    public List<string> Stack { get; private set; } = [];
    public string? CompanyDomain { get; private set; }
    public bool Enriched { get; private set; }
    public uint Version { get; private set; }

    public static JobPosting Discover(Guid campaignId, DiscoveredPosting p, DateTime now)
    {
        var job = new JobPosting
        {
            Id = Guid.CreateVersion7(),
        CampaignId = campaignId,
        Source = Cut(p.Source, 300),
        ExternalId = Cut(p.ExternalId, 120),
        CanonicalKey = Fingerprint.For(p.Company, p.Title, p.Location, p.ApplyUrl),
        Company = Cut(p.Company.Trim(), 200),
        Title = Cut(p.Title.Trim(), 300),
        Location = Cut(p.Location.Trim(), 200),
        ApplyUrl = Cut(p.ApplyUrl.Trim(), 1000),
        Description = p.Description.Length > 20_000 ? p.Description[..20_000] : p.Description,
        SourceUpdatedAt = p.UpdatedAt,
        FirstSeenAt = now,
        LastSeenAt = now,
        Status = JobStatus.Discovered
        };
        job.Apply(Normalizer.From(job.Title, job.Description, job.Location, job.ApplyUrl, p.Workplace, p.Compensation, p.Employment, p.ApplyEmail));
        return job;
    }

    /// <summary>A fonte é a verdade mais recente: nome, cargo e local se atualizam; a identidade canônica não muda.</summary>
    public void SeenAgain(DiscoveredPosting p, DateTime now)
    {
        Company = Cut(p.Company.Trim(), 200);
        Title = Cut(p.Title.Trim(), 300);
        Location = Cut(p.Location.Trim(), 200);
        LastSeenAt = now;
        SourceUpdatedAt = p.UpdatedAt;
        Description = p.Description.Length > 20_000 ? p.Description[..20_000] : p.Description;
        Apply(Normalizer.From(Title, Description, Location, ApplyUrl, p.Workplace, p.Compensation, p.Employment, p.ApplyEmail));
    }

    /// <summary>Fonte externa não define o tamanho dos nossos campos: corta no limite da coluna, sem derrubar a rodada.</summary>
    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];

    private void Apply(Normalized n)
    {
        Seniority = n.Seniority;
        Modality = n.Modality;
        SalaryText = n.SalaryText?.Length > 120 ? n.SalaryText[..120] : n.SalaryText;
        SalaryMin = n.SalaryMin;
        SalaryMax = n.SalaryMax;
        SalaryCurrency = n.Currency;
        Contract = n.Contract;
        English = n.English;
        Sponsorship = n.Sponsorship;
        ApplyChannel = n.ApplyChannel;
        ApplyEmail = n.ApplyEmail is null ? null : Cut(n.ApplyEmail, 200);
        Enrich();
    }

    /// <summary>Stack pedida e domínio da empresa (para a logo), tirados do texto da vaga.</summary>
    /// <summary>Recalcula a chave de duplicidade com a regra atual (as vagas antigas ganham a chave nova uma vez, na subida).</summary>
    public void Rekey() => CanonicalKey = Fingerprint.For(Company, Title, Location, ApplyUrl);

    public void Enrich()
    {
        Stack = Domain.Stack.Detect(Title, Description);
        CompanyDomain = Domain.CompanyDomain.Find(Description, ApplyUrl);
        Enriched = true;
    }

    /// <summary>A mesma vaga já chegou por outra fonte: fica registrada, mas não gera candidatura.</summary>
    public void MarkDuplicate(Guid original)
    {
        Status = JobStatus.Duplicate;
        DuplicateOf = original;
        Reason = "Mesma vaga já encontrada por outra fonte.";
    }

    public void Verified(PostingStatus status, DateTime now)
    {
        // Reabertura: vaga encerrada que volta a aparecer aberta é avaliada de novo (a candidatura antiga, se houver, continua única).
        if (status.Open && Status == JobStatus.Expired)
        {
            Status = JobStatus.Discovered;
            Reason = "Reaberta: voltou a aparecer aberta na fonte.";
        }
        Open = status.Open;
        LastVerifiedAt = now;
        if (!status.Open && Status is not JobStatus.Duplicate)
        {
            Status = JobStatus.Expired;
            Reason = status.Detail;
        }
    }

    /// <summary>Aplica elegibilidade e nota. Devolve true só na primeira vez que a vaga entra na fila.</summary>
    public bool Evaluate(Evaluation e, int minFitScore)
    {
        if (Status is JobStatus.Duplicate or JobStatus.Expired or JobStatus.Shortlisted) return false;
        Score = e.Score;
        Confidence = e.Confidence;
        Breakdown = JsonSerializer.Serialize(e.Factors, JsonSerializerOptions.Web);
        if (e.Rejection is not null)
        {
            Status = JobStatus.Rejected;
            Reason = e.Rejection;
            return false;
        }
        if (e.Score < minFitScore)
        {
            Status = JobStatus.Scored;
            Reason = $"Nota {e.Score} abaixo do corte da campanha ({minFitScore}).";
            return false;
        }
        Status = JobStatus.Shortlisted;
        Reason = null;
        return true;
    }
}

/// <summary>Cópia do que a fonte devolveu, guardada só quando muda.</summary>
internal sealed class JobSnapshot
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public DateTime FetchedAt { get; set; }
    public string PayloadHash { get; set; } = "";
    public string Payload { get; set; } = "{}";
}

/// <summary>
/// Identidade canônica de uma vaga. Com id de vaga do ATS no link, a identidade é o ATS + id (vale entre fontes);
/// sem isso, é o hash de empresa, cargo e local normalizados.
/// </summary>
internal static partial class Fingerprint
{
    public static string For(string company, string title, string location, string applyUrl)
    {
        if (Uri.TryCreate(applyUrl, UriKind.Absolute, out var uri) && AtsJob().Match(uri.AbsolutePath) is { Success: true } m)
            return $"ats:{uri.Authority.ToLowerInvariant()}:{m.Groups[1].Value}";
        // A mesma vaga chega por quadros diferentes com a empresa, o cargo e o local escritos de jeitos diferentes ("Acme Tecnologia Ltda" e
        // "Acme"; "Desenvolvedor .NET Sr (Remoto)" e "Desenvolvedor .NET Sênior"; "Rio de Janeiro, RJ, BR" e "Rio de Janeiro"): a chave usa a forma curta.
        var basis = $"{Company(company)}|{Title(title)}|{City(location)}";
        return "fp2:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(basis)))[..32];
    }

    /// <summary>Nome da empresa sem sufixo jurídico, país e palavras genéricas de ramo ("Accenture do Brasil Ltda" e "Accenture" dão "accenture").</summary>
    internal static string Company(string company)
    {
        var words = Normalize(company).Split(" ", StringSplitOptions.RemoveEmptyEntries).Where(w => !CompanyNoise.Contains(w.Replace(".", ""))).ToList();
        return words.Count == 0 ? Normalize(company) : string.Join(' ', words.Take(2));
    }

    /// <summary>Cargo sem modalidade, parênteses e marcas de anúncio; nível escrito de um jeito só (sr, sênior e senior dão "senior").</summary>
    internal static string Title(string title)
    {
        var text = Normalize(Bracketed().Replace(title, " "));
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => Level.TryGetValue(w.Trim('.'), out var l) ? l : w).Where(w => !TitleNoise.Contains(w)).ToList();
        return string.Join(' ', words);
    }

    /// <summary>Só a cidade (o que vem antes da vírgula); remoto em qualquer língua vira "remoto".</summary>
    internal static string City(string location)
    {
        var text = Normalize(location);
        if (text.Length == 0 || Remote().IsMatch(text)) return text.Length == 0 ? "" : "remoto";
        return Normalize(location.Split([',', '-', '/', '|', ';', '('], 2)[0]);
    }

    private static readonly HashSet<string> CompanyNoise = ["ltda", "ltd", "sa", "s", "a", "inc", "llc", "gmbh", "eireli", "me", "epp", "corp", "corporation", "co", "the",
        "do", "da", "de", "brasil", "brazil", "br", "tecnologia", "technology", "technologies", "tech", "solutions", "solucoes", "servicos", "services", "consultoria",
        "consulting", "digital", "software", "sistemas", "systems", "group", "grupo", "holding", "company", "labs"];
    private static readonly HashSet<string> TitleNoise = ["remoto", "remote", "hibrido", "hybrid", "presencial", "onsite", "vaga", "oportunidade", "afirmativa", "pcd",
        "home", "office", "100%", "temporaria", "urgente", "para", "-", "|", "/"];
    private static readonly Dictionary<string, string> Level = new() { ["sr"] = "senior", ["pl"] = "pleno", ["jr"] = "junior", ["mid"] = "pleno", ["middle"] = "pleno" };

    internal static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var plain = new string([.. decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)]);
        return Spaces().Replace(NonWord().Replace(plain, " "), " ").Trim();
    }

    [GeneratedRegex(@"/jobs/(\d+)")] private static partial Regex AtsJob();
    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]")] private static partial Regex Bracketed();
    [GeneratedRegex(@"\b(remot[oa]|remote|home office|anywhere|qualquer lugar|teletrabalho)\b")] private static partial Regex Remote();
    [GeneratedRegex(@"[^a-z0-9#+.]+")] private static partial Regex NonWord();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}
