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
    /// <summary>Cargos-alvo (de qualquer área), comparados com o título da vaga na nota.</summary>
    public List<string> Titles { get; private set; } = [];
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

    /// <summary>Liga o envio automático: só com uma autorização ativa, criada na tela (nunca por configuração).</summary>
    public void EnableAuto(SubmitAuthorization authorization, DateTime now)
    {
        if (authorization.CampaignId != Id || !authorization.Active(now)) throw new DomainRuleException("A autorização não vale para esta campanha agora.");
        Mode = SubmissionMode.AutoSubmit;
        UpdatedAt = now;
    }

    /// <summary>Revogada ou vencida: volta para a revisão sua, que nunca envia sem o seu ok.</summary>
    public void DisableAuto(DateTime now)
    {
        if (Mode != SubmissionMode.AutoSubmit) return;
        Mode = SubmissionMode.ReviewRequired;
        UpdatedAt = now;
    }

    /// <summary>Envio automático não se liga por aqui: só por autorização dada no painel.</summary>
    public void Configure(SubmissionMode mode, int minFitScore, int maxSubmissionsPerDay, IEnumerable<string> technologies, IEnumerable<string> locations, IEnumerable<string> sources, DateTime now)
    {
        // Automático não se liga por aqui; se já está ligado (por autorização), salvar outros critérios mantém.
        if (mode == SubmissionMode.AutoSubmit && Mode != SubmissionMode.AutoSubmit) throw new DomainRuleException("O envio automático exige uma autorização dada no painel.");
        Mode = mode;
        MinFitScore = minFitScore;
        MaxSubmissionsPerDay = maxSubmissionsPerDay;
        Technologies = [.. technologies.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        Locations = [.. locations.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        Sources = [.. sources.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal)];
        UpdatedAt = now;
    }

    /// <summary>Fontes achadas pelo Explorador: entram no fim da lista, sem repetir.</summary>
    public int AddSources(IEnumerable<string> sources, DateTime now)
    {
        var fresh = sources.Select(s => s.Trim()).Where(s => s.Length > 0 && !Sources.Contains(s, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
        if (fresh.Count == 0) return 0;
        Sources = [.. Sources, .. fresh];
        UpdatedAt = now;
        return fresh.Count;
    }

    /// <summary>Tira fontes que saíram do ar; devolve quantas saíram.</summary>
    public int RemoveSources(IEnumerable<string> sources, DateTime now)
    {
        var gone = sources.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = Sources.Where(s => !gone.Contains(s)).ToList();
        var removed = Sources.Count - kept.Count;
        if (removed == 0) return 0;
        Sources = kept;
        UpdatedAt = now;
        return removed;
    }

    /// <summary>Busca da noite: o timer só pede rodada de campanha ligada.</summary>
    public void Schedule(bool enabled, DateTime now) { Enabled = enabled; UpdatedAt = now; }

    public void Target(IEnumerable<string> titles, DateTime now)
    {
        Titles = [.. titles.Select(t => t.Trim()).Where(t => t.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).Take(8)];
        UpdatedAt = now;
    }

    /// <summary>O nome que a pessoa vê (a área da busca, quando vem do primeiro contato).</summary>
    public void Rename(string name) => Name = name.Trim().Length > 80 ? name.Trim()[..80] : name.Trim();

    public CampaignRun RequestRun(string trigger, DateTime now) => CampaignRun.Create(Id, trigger, now);

    /// <summary>A lista de empresas cresce sozinha: um link salvo de ATS adiciona o board. Devolve false se já era vigiado.</summary>
    public bool Watch(string source, DateTime now)
    {
        if (Sources.Contains(source, StringComparer.OrdinalIgnoreCase)) return false;
        Sources = [.. Sources, source];
        UpdatedAt = now;
        return true;
    }
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

/// <summary>Autorização de envio automático: escopo (campanha e adapters), limites (por dia, nota mínima), validade e revogação.</summary>
internal sealed class SubmitAuthorization
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid CampaignId { get; init; }
    public DateTime GrantedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public int MaxPerDay { get; init; }
    public int MinScore { get; init; }
    public List<string> Adapters { get; init; } = [];
    /// <summary>The privacy, data processing and terms boxes of application forms may be ticked in your name.</summary>
    public bool AcceptDeclarations { get; init; }
    public string Text { get; init; } = "";
    public DateTime? RevokedAt { get; set; }
    public bool Active(DateTime now) => RevokedAt is null && now < ExpiresAt;
}
