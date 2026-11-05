namespace JobOrchestrator.Campaigns.Domain;

/// <summary>Agregado: uma campanha de busca, com critérios, limites e até onde pode ir sozinha.</summary>
internal sealed class Campaign : AggregateRoot
{
    private Campaign() { }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public bool Enabled { get; private set; }
    public SubmissionMode Mode { get; private set; }
    public int MinFitScore { get; private set; }
    public int MaxSubmissionsPerDay { get; private set; }
    public List<string> Technologies { get; private set; } = [];
    public List<string> Locations { get; private set; } = [];
    public List<string> Sources { get; private set; } = [];
    public string TimeZone { get; private set; } = "America/Sao_Paulo";
    public string Window { get; private set; } = "23:00-08:00";
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    /// <summary>Toda campanha nasce só preparando: enviar exige mudança explícita do dono.</summary>
    public static Campaign Create(string name, IEnumerable<string> technologies, IEnumerable<string> locations, IEnumerable<string> sources, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = name,
        Enabled = false,
        Mode = SubmissionMode.PrepareOnly,
        MinFitScore = 85,
        MaxSubmissionsPerDay = 15,
        Technologies = [.. technologies],
        Locations = [.. locations],
        Sources = [.. sources],
        CreatedAt = now,
        UpdatedAt = now
    };

    /// <summary>Envio automático não se liga por aqui: só por autorização dada no painel (fase 6).</summary>
    public void Configure(SubmissionMode mode, int minFitScore, int maxSubmissionsPerDay, IEnumerable<string> technologies, IEnumerable<string> locations, IEnumerable<string> sources, DateTime now)
    {
        if (mode == SubmissionMode.AutoSubmit) throw new DomainRuleException("O envio automático exige uma autorização dada no painel.");
        Mode = mode;
        MinFitScore = minFitScore;
        MaxSubmissionsPerDay = maxSubmissionsPerDay;
        Technologies = [.. technologies.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        Locations = [.. locations.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        Sources = [.. sources.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal)];
        UpdatedAt = now;
    }

    public CampaignRun RequestRun(string trigger, DateTime now) => CampaignRun.Create(Id, trigger, now);
}

/// <summary>Uma rodada de descoberta pedida para a campanha.</summary>
internal sealed class CampaignRun
{
    private CampaignRun() { }
    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public string Trigger { get; private set; } = "";
    public DateTime RequestedAt { get; private set; }

    internal static CampaignRun Create(Guid campaignId, string trigger, DateTime now) =>
        new() { Id = Guid.CreateVersion7(), CampaignId = campaignId, Trigger = trigger, RequestedAt = now };
}
