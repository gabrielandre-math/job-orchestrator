namespace JobOrchestrator.Profile.Features;

/// <summary>Um documento como a tela mostra.</summary>
internal sealed record DocumentView
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = "";
    public string Kind { get; init; } = "";
    public int Pages { get; init; }
    public string Status { get; init; } = "";
    public string? Error { get; init; }
    public string? ExtractedBy { get; init; }
    public DateTime ImportedAt { get; init; }
    public DateTime? ExtractedAt { get; init; }
}

/// <summary>Um fato como a tela mostra. Propriedades, não construtor: o Npgsql entrega text[] como Array.</summary>
internal sealed record FactView
{
    public Guid Id { get; init; }
    public Guid? DocumentId { get; init; }
    public string Kind { get; init; } = "";
    public string Statement { get; init; } = "";
    public Guid? ParentId { get; init; }
    public string? Organization { get; init; }
    public string? Role { get; init; }
    public string? Start { get; init; }
    public string? End { get; init; }
    public string[] Technologies { get; init; } = [];
    public string? Level { get; init; }
    public string? Quote { get; init; }
    public int? Page { get; init; }
    public string? Note { get; init; }
    public string Status { get; init; } = "";
}

internal sealed record ProfileRow
{
    public Guid Id { get; init; }
    public int Version { get; init; }
    public string Status { get; init; } = "";
    public bool Synthetic { get; init; }
    public string FullName { get; init; } = "";
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Location { get; init; }
    public string? LinkedIn { get; init; }
    public string? GitHub { get; init; }
    public string? Website { get; init; }
    public string Confirmations { get; init; } = "{}";
    public DateTime? ConfirmedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>O perfil para a tela: dados pessoais, confirmações, documentos e fatos com evidência.</summary>
internal sealed record ProfileView(
    Guid? Id, int Version, string Status, bool Synthetic, Identity Identity, Confirmations Confirmations, DateTime? ConfirmedAt, DateTime? UpdatedAt,
    IReadOnlyList<DocumentView> Documents, IReadOnlyList<FactView> Facts, int Pending);

/// <summary>Consulta (CQRS): o perfil inteiro.</summary>
internal sealed record GetProfileQuery : IQuery<ProfileView>;

internal sealed class GetProfileHandler(IReadDb<ProfileDbContext> db) : IQueryHandler<GetProfileQuery, ProfileView>
{
    public async Task<Result<ProfileView>> Handle(GetProfileQuery q, CancellationToken ct)
    {
        var p = await db.FirstAsync<ProfileRow>("""
            select id, version, status, synthetic, full_name, email, phone, location, linked_in, git_hub, website, confirmations::text as confirmations, confirmed_at, updated_at
            from profile.profiles order by created_at limit 1
            """, null, ct);
        if (p is null)
            return new ProfileView(null, 0, nameof(ProfileStatus.Empty), false, new Identity("", null, null, null, null, null, null), Confirmations.Empty, null, null, [], [], 0);
        var docs = await db.ListAsync<DocumentView>("""
            select id, file_name, kind, pages, status, error, extracted_by, imported_at, extracted_at from profile.documents where profile_id = @Id order by imported_at desc
            """, new { p.Id }, ct);
        var facts = await db.ListAsync<FactView>("""
            select id, document_id, kind, statement, parent_id, organization, role, start, "end", technologies, level, quote, page, note, status
            from profile.facts where profile_id = @Id order by position
            """, new { p.Id }, ct);
        var confirmations = p.Confirmations is { Length: > 2 } j ? JsonSerializer.Deserialize<Confirmations>(j, JsonSerializerOptions.Web) ?? Confirmations.Empty : Confirmations.Empty;
        return new ProfileView(p.Id, p.Version, p.Status, p.Synthetic, new Identity(p.FullName, p.Email, p.Phone, p.Location, p.LinkedIn, p.GitHub, p.Website),
            confirmations, p.ConfirmedAt, p.UpdatedAt, docs, facts, facts.Count(f => f.Status == nameof(FactStatus.Inferred)));
    }
}

/// <summary>Comando: seus dados pessoais. Ficam só aqui; entram no PDF e no formulário, nunca no prompt.</summary>
internal sealed record SetIdentityCommand(string FullName, string? Email, string? Phone, string? Location, string? LinkedIn, string? GitHub, string? Website)
    : ICommand<Done>, IProfileCommand;

internal sealed class SetIdentityValidator : AbstractValidator<SetIdentityCommand>
{
    public SetIdentityValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(160).WithMessage("Informe o nome completo.");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("E-mail inválido.");
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.LinkedIn).Must(Url).When(x => !string.IsNullOrWhiteSpace(x.LinkedIn)).WithMessage("Link do LinkedIn inválido.");
        RuleFor(x => x.GitHub).Must(Url).When(x => !string.IsNullOrWhiteSpace(x.GitHub)).WithMessage("Link do GitHub inválido.");
        RuleFor(x => x.Website).Must(Url).When(x => !string.IsNullOrWhiteSpace(x.Website)).WithMessage("Link do site inválido.");
    }

    private static bool Url(string? s) => Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";
}

internal sealed class SetIdentityHandler(ProfileDbContext db, TimeProvider time) : ICommandHandler<SetIdentityCommand, Done>
{
    public async Task<Result<Done>> Handle(SetIdentityCommand c, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var profile = await db.CurrentAsync(ct);
        if (profile is null) { profile = CandidateProfile.Create(now); db.Profiles.Add(profile); }
        profile.SetIdentity(new Identity(c.FullName, c.Email, c.Phone, c.Location, c.LinkedIn, c.GitHub, c.Website), now);
        return new Done();
    }
}

/// <summary>Comando: a tela única. Decide cada fato inferido e responde o que só você pode afirmar.</summary>
internal sealed record ConfirmProfileCommand(Confirmations Confirmations, Dictionary<Guid, bool>? Decisions) : ICommand<Done>, IProfileCommand;

internal sealed class ConfirmProfileValidator : AbstractValidator<ConfirmProfileCommand>
{
    private static readonly string[] Modalities = ["remote", "hybrid", "onsite"];
    private static readonly string[] English = ["basic", "intermediate", "advanced", "fluent", "native"];

    public ConfirmProfileValidator()
    {
        RuleFor(x => x.Confirmations).NotNull();
        RuleFor(x => x.Confirmations.AuthorizedCountries).NotEmpty().WithMessage("Informe ao menos um país onde você pode trabalhar.");
        RuleFor(x => x.Confirmations.Modalities).NotEmpty().Must(m => m.All(Modalities.Contains)).WithMessage("Escolha ao menos uma modalidade.");
        RuleFor(x => x.Confirmations.FloorCltBrl).InclusiveBetween(1000, 200_000).When(x => x.Confirmations.FloorCltBrl is not null).WithMessage("O piso CLT vai de R$ 1.000 a R$ 200.000 por mês.");
        RuleFor(x => x.Confirmations.FloorPjBrl).InclusiveBetween(1000, 300_000).When(x => x.Confirmations.FloorPjBrl is not null).WithMessage("O piso PJ vai de R$ 1.000 a R$ 300.000 por mês.");
        RuleFor(x => x.Confirmations.FloorUsdMonth).InclusiveBetween(500, 60_000).When(x => x.Confirmations.FloorUsdMonth is not null).WithMessage("O piso em dólar vai de US$ 500 a US$ 60.000 por mês.");
        RuleFor(x => x.Confirmations.EnglishLevel).Must(e => e is null || English.Contains(e)).WithMessage("Nível de inglês desconhecido.");
        RuleFor(x => x.Confirmations.SelfIdDefault).Must(s => s is "prefer_not" or "answer_case_by_case").WithMessage("Resposta padrão de autoidentificação desconhecida.");
        RuleFor(x => x.Confirmations.OpenAnswersPolicy).Must(s => s is "review" or "auto_after_check").WithMessage("Política de respostas abertas desconhecida.");
        RuleFor(x => x.Confirmations.StartAvailability).MaximumLength(80);
    }
}

internal sealed class ConfirmProfileHandler(ProfileDbContext db, TimeProvider time) : ICommandHandler<ConfirmProfileCommand, Done>
{
    public async Task<Result<Done>> Handle(ConfirmProfileCommand c, CancellationToken ct)
    {
        var profile = await db.CurrentAsync(ct);
        if (profile is null) return Error.NotFound("profile-not-found", "Envie o seu currículo primeiro.");
        var decisions = c.Decisions ?? [];
        var open = profile.Facts.Where(f => f.Status == FactStatus.Inferred).Select(f => f.Id).Where(id => !decisions.ContainsKey(id)).ToList();
        if (open.Count > 0) return Error.Validation("facts-pending", $"Falta decidir {open.Count} {(open.Count == 1 ? "fato" : "fatos")} antes de confirmar.");
        if (profile.FullName.Length == 0) return Error.Validation("name-missing", "Informe o seu nome nos dados pessoais antes de confirmar.");
        profile.Confirm(c.Confirmations with { Cities = [.. c.Confirmations.Cities.Select(x => x.Trim()).Where(x => x.Length > 0)] }, decisions, time.GetUtcNow().UtcDateTime);
        return new Done();
    }
}

/// <summary>Rotas HTTP (Carter) do perfil.</summary>
internal sealed class ProfileEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profile").WithTags("Profile");
        group.MapGet("", async (ISender sender, CancellationToken ct) => (await sender.Send(new GetProfileQuery(), ct)).ToHttp(Results.Ok));
        group.MapPost("documents", async (IFormFile file, ISender sender, CancellationToken ct) =>
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            return (await sender.Send(new ImportDocumentCommand(file.FileName, buffer.ToArray()), ct)).ToHttp(v => Results.Accepted(value: v));
        }).DisableAntiforgery();
        group.MapPost("documents/{id:guid}/retry", async (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new RetryDocumentCommand(id), ct)).ToHttp(_ => Results.Accepted()));
        group.MapPut("identity", async (SetIdentityCommand body, ISender sender, CancellationToken ct) =>
            (await sender.Send(body, ct)).ToHttp(_ => Results.NoContent()));
        group.MapPut("confirmations", async (ConfirmProfileCommand body, ISender sender, CancellationToken ct) =>
            (await sender.Send(body, ct)).ToHttp(_ => Results.NoContent()));
        group.MapGet("answers", async (string? lang, string? contract, string? currency, string? country, string? tech, IProfileApi api, CancellationToken ct) =>
        {
            var context = new AnswerContext(contract, currency, country, (tech ?? "C#,.NET,AWS,PostgreSQL").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                lang == "en" ? "en" : "pt", "greenhouse");
            return Results.Ok(await api.AnswersAsync(context, ct));
        });
    }
}
