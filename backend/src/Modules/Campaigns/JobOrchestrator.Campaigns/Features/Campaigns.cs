namespace JobOrchestrator.Campaigns.Features;

/// <summary>Uma campanha como a tela mostra. Propriedades, não construtor: o Npgsql entrega text[] como Array.</summary>
internal sealed record CampaignView
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public bool Enabled { get; init; }
    public string Mode { get; init; } = "";
    public int MinFitScore { get; init; }
    public int MaxSubmissionsPerDay { get; init; }
    public string[] Technologies { get; init; } = [];
    public string[] Titles { get; init; } = [];
    public string[] Locations { get; init; } = [];
    public string[] Sources { get; init; } = [];
    public string TimeZone { get; init; } = "";
    public string Window { get; init; } = "";
    public DateTime? LastRunAt { get; init; }
}

/// <summary>Consulta (CQRS): todas as campanhas.</summary>
internal sealed record ListCampaignsQuery : IQuery<IReadOnlyList<CampaignView>>;

internal sealed class ListCampaignsHandler(IReadDb<CampaignsDbContext> db) : IQueryHandler<ListCampaignsQuery, IReadOnlyList<CampaignView>>
{
    internal const string Sql = """
        select c.id, c.name, c.enabled, c.mode, c.min_fit_score, c.max_submissions_per_day, c.technologies, c.titles, c.locations, c.sources,
               c.time_zone, c.window, (select max(r.requested_at) from campaigns.runs r where r.campaign_id = c.id) as last_run_at
        from campaigns.campaigns c order by c.created_at
        """;

    public async Task<Result<IReadOnlyList<CampaignView>>> Handle(ListCampaignsQuery q, CancellationToken ct) =>
        Result<IReadOnlyList<CampaignView>>.Ok(await db.ListAsync<CampaignView>(Sql, null, ct));
}

/// <summary>Comando: muda critérios, limites e modo da campanha.</summary>
internal sealed record ConfigureCampaignCommand(Guid Id, string Mode, int MinFitScore, int MaxSubmissionsPerDay, string[] Technologies, string[] Locations, string[] Sources, string? Name = null, string[]? Titles = null)
    : ICommand<Done>, ICampaignsCommand;

internal sealed class ConfigureCampaignValidator : AbstractValidator<ConfigureCampaignCommand>
{
    public ConfigureCampaignValidator()
    {
        RuleFor(x => x.Mode).Must(m => Enum.TryParse<SubmissionMode>(m, out _)).WithMessage("Modo desconhecido.");
        RuleFor(x => x.MinFitScore).InclusiveBetween(0, 100).WithMessage("A nota mínima vai de 0 a 100.");
        RuleFor(x => x.MaxSubmissionsPerDay).InclusiveBetween(0, 100).WithMessage("O limite diário vai de 0 a 100.");
    }
}

internal sealed class ConfigureCampaignHandler(CampaignsDbContext db, TimeProvider time) : ICommandHandler<ConfigureCampaignCommand, Done>
{
    public async Task<Result<Done>> Handle(ConfigureCampaignCommand c, CancellationToken ct)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == c.Id, ct);
        if (campaign is null) return Error.NotFound("campaign-not-found", "Campanha não encontrada.");
        var mode = Enum.Parse<SubmissionMode>(c.Mode);
        if (mode == SubmissionMode.AutoSubmit && campaign.Mode != SubmissionMode.AutoSubmit)
            return Error.Forbidden("consent-required", "O envio automático só liga com uma autorização criada na tela de autorizações.");
        campaign.Configure(mode, c.MinFitScore, c.MaxSubmissionsPerDay, c.Technologies, c.Locations, c.Sources, time.GetUtcNow().UtcDateTime);
        if (c.Name is { Length: > 0 } name) campaign.Rename(name);
        if (c.Titles is { } titles) campaign.Target(titles, time.GetUtcNow().UtcDateTime);
        return new Done();
    }
}

/// <summary>A rodada criada.</summary>
internal sealed record RunView(Guid RunId, DateTime RequestedAt);

/// <summary>Comando: pede uma rodada de descoberta agora. O Worker faz o trabalho.</summary>
internal sealed record RequestRunCommand(Guid CampaignId) : ICommand<RunView>, ICampaignsCommand;

internal sealed class RequestRunHandler(CampaignsDbContext db, TimeProvider time) : ICommandHandler<RequestRunCommand, RunView>
{
    public async Task<Result<RunView>> Handle(RequestRunCommand c, CancellationToken ct)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == c.CampaignId, ct);
        if (campaign is null) return Error.NotFound("campaign-not-found", "Campanha não encontrada.");
        if (campaign.Sources.Count == 0) return Error.Validation("no-sources", "Cadastre ao menos uma fonte antes de rodar.");
        var now = time.GetUtcNow().UtcDateTime;
        var run = campaign.RequestRun("manual", now);
        db.Runs.Add(run);
        db.Publish(RunRequestedV1.EventType, new RunRequestedV1(run.Id, campaign.Id, now), now);
        return new RunView(run.Id, now);
    }
}

/// <summary>Rotas HTTP (Carter) das campanhas.</summary>
internal sealed class CampaignEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/campaigns").WithTags("Campaigns");
        group.MapGet("", async (ISender sender, CancellationToken ct) => (await sender.Send(new ListCampaignsQuery(), ct)).ToHttp(Results.Ok));
        group.MapPut("{id:guid}", async (Guid id, ConfigureCampaignCommand body, ISender sender, CancellationToken ct) =>
            (await sender.Send(body with { Id = id }, ct)).ToHttp(_ => Results.NoContent()));
        group.MapPost("{id:guid}/runs", async (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new RequestRunCommand(id), ct)).ToHttp(v => Results.Accepted(value: v)));
    }
}
