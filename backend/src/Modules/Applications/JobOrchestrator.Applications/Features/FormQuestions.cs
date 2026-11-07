namespace JobOrchestrator.Applications.Features;

internal sealed record PendingFormQuestion(string Key, string Label, string Kind, IReadOnlyList<string> Options, IReadOnlyList<Guid> Applications);
internal sealed record SavedFormAnswers(int Resumed);
internal sealed record SaveFormAnswersCommand(IReadOnlyList<AnswerInput> Answers) : ICommand<SavedFormAnswers>, IApplicationsCommand;

internal sealed class SaveFormAnswersValidator : AbstractValidator<SaveFormAnswersCommand>
{
    public SaveFormAnswersValidator(TimeProvider time)
    {
        RuleFor(c => c.Answers).NotNull().Must(a => a is { Count: > 0 and <= 30 } && a.All(x => x is not null)).WithMessage("Responda até 30 perguntas por vez.");
        RuleForEach(c => c.Answers).ChildRules(a =>
        {
            a.RuleFor(x => x.Question).NotEmpty().MaximumLength(600);
            a.RuleFor(x => x.Answer).MaximumLength(2000);
            a.RuleFor(x => x.Answer).Must(value => string.IsNullOrWhiteSpace(value) || PersonalAnswer.TryBirthDate(value, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), out _))
                .When(x => PersonalAnswer.IsBirthDate(x.Question)).WithMessage("Informe uma data de nascimento válida, sem estar no futuro (AAAA-MM-DD).");
        });
    }
}

/// <summary>Read the structured questions from the latest stop, rather than parsing the translated reason text.</summary>
internal sealed class FormQuestions(ApplicationsDbContext db)
{
    private sealed record Stop(IReadOnlyList<FormQuestion>? Questions);
    internal sealed record Waiting(JobApplication Application, IReadOnlyList<FormQuestion> Questions);

    internal async Task<IReadOnlyList<Waiting>> WaitingAsync(CancellationToken ct)
    {
        var apps = await db.Applications.Where(a => a.State == ApplicationState.NeedsHuman && a.ArchivedAt == null && a.ResumePath != null
            && db.Tasks.Any(t => t.ApplicationId == a.Id && t.ResolvedAt == null && t.Kind == HumanTaskKind.Question)
            && !db.Tasks.Any(t => t.ApplicationId == a.Id && t.ResolvedAt == null && t.Kind != HumanTaskKind.Question)).ToListAsync(ct);
        var ids = apps.Select(a => a.Id).ToArray();
        var events = await db.Events.AsNoTracking().Where(e => ids.Contains(e.ApplicationId) && e.To == "NeedsHuman")
            .OrderByDescending(e => e.Id).Select(e => new { e.ApplicationId, e.Detail }).ToListAsync(ct);
        var last = events.DistinctBy(e => e.ApplicationId).ToDictionary(e => e.ApplicationId);
        var waiting = new List<Waiting>();
        foreach (var app in apps)
        {
            if (!last.TryGetValue(app.Id, out var e) || e.Detail is null) continue;
            try
            {
                if (JsonSerializer.Deserialize<Stop>(e.Detail, JsonSerializerOptions.Web)?.Questions is { Count: > 0 } questions)
                    waiting.Add(new(app, questions));
            }
            catch (JsonException) { /* Legacy stops with no structured questions remain visible in Attention. */ }
        }
        return waiting;
    }

    /// <summary>The answers to a send the app clicked but could not confirm: only you know if it arrived.</summary>
    internal static readonly string[] Arrival = ["Chegou", "Não chegou, pode enviar de novo", "Ainda não sei"];

    public async Task<IReadOnlyList<PendingFormQuestion>> PendingAsync(CancellationToken ct) =>
        [.. await FormAsync(ct), .. await UncertainAsync(ct)];

    private async Task<IReadOnlyList<PendingFormQuestion>> UncertainAsync(CancellationToken ct) =>
        [.. (await db.Applications.AsNoTracking().Where(a => a.State == ApplicationState.NeedsHuman && a.ArchivedAt == null
                && db.Tasks.Any(t => t.ApplicationId == a.Id && t.ResolvedAt == null && t.Kind == HumanTaskKind.Reconcile)).ToListAsync(ct))
            .Select(a => new PendingFormQuestion($"arrived:{a.Id}", $"A candidatura para {a.Title} na {a.Company} chegou? O app clicou em enviar, mas não viu a confirmação.", "arrived", Arrival, [a.Id]))];

    private async Task<IReadOnlyList<PendingFormQuestion>> FormAsync(CancellationToken ct) =>
        (await WaitingAsync(ct)).SelectMany(w => w.Questions.Select(q => new { w.Application.Id, Question = q }))
            .GroupBy(x => new { Key = CustomAnswer.Key(x.Question.Label), Kind = PersonalAnswer.IsBirthDate(x.Question.Label) ? "date" : x.Question.Kind, Options = string.Join('\n', x.Question.Options) })
            .Select(g => new PendingFormQuestion(g.Key.Key, g.First().Question.Label, PersonalAnswer.IsBirthDate(g.First().Question.Label) ? "date" : g.Key.Kind,
                g.First().Question.Options, g.Select(x => x.Id).Distinct().ToList())).OrderBy(q => q.Label).ToList();

    public async Task<int> ResumeAsync(IReadOnlySet<string> keys, DateTime now, CancellationToken ct)
    {
        var count = 0;
        foreach (var w in await WaitingAsync(ct))
        {
            if (!w.Questions.Any(q => keys.Contains(CustomAnswer.Key(q.Label)))) continue;
            var app = w.Application;
            // A double click or a second tab cannot create another live Prepare item. Never queue Submit here.
            if (await db.WorkItems.AnyAsync(i => i.ApplicationId == app.Id && i.Step == WorkStep.Prepare && i.CompletedAt == null, ct)) continue;
            await db.ResolveTasksAsync(app.Id, "Resposta salva; retomando o preparo", now, ct);
            app.Move(ApplicationState.FormPreparing, "Resposta salva por você. Retomando o preenchimento com as autorizações e limites atuais.", "Você", now);
            db.WorkItems.Add(WorkItem.Create(app.Id, WorkStep.Prepare, now, now));
            count++;
        }
        return count;
    }
}

internal sealed class SaveFormAnswersHandler(ApplicationsDbContext db, TimeProvider time) : ICommandHandler<SaveFormAnswersCommand, SavedFormAnswers>
{
    public async Task<Result<SavedFormAnswers>> Handle(SaveFormAnswersCommand c, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var arrivals = c.Answers.Where(a => a.Key is { } k && k.StartsWith("arrived:", StringComparison.Ordinal)).ToList();
        var form = c.Answers.Except(arrivals).ToList();
        AnswerStore.SaveMany(form, now);
        var keys = form.Where(a => !string.IsNullOrWhiteSpace(a.Answer)).Select(a => CustomAnswer.Key(a.Question)).ToHashSet(StringComparer.Ordinal);
        var resumed = keys.Count == 0 ? 0 : await new FormQuestions(db).ResumeAsync(keys, now, ct);
        foreach (var a in arrivals)
            if (Guid.TryParse(a.Key!["arrived:".Length..], out var id) && await ArrivedAsync(id, a.Answer, now, ct)) resumed++;
        return new SavedFormAnswers(resumed);
    }

    /// <summary>
    /// "Chegou": the send is yours to vouch for (declared, never confirmed by the site). "Não chegou": the open intent is discarded
    /// and the form goes back to preparing, under the current mode and limits. "Ainda não sei": nothing changes.
    /// </summary>
    private async Task<bool> ArrivedAsync(Guid id, string? answer, DateTime now, CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == id && a.State == ApplicationState.NeedsHuman, ct);
        if (app is null) return false;
        if (answer == FormQuestions.Arrival[0])
        {
            await db.ResolveTasksAsync(app.Id, "Você confirmou que chegou", now, ct);
            app.Move(ApplicationState.SubmittedDeclared, "Você confirmou que o envio feito pelo app chegou.", "Você", now);
            db.Evidence.Add(new Evidence { Id = Guid.CreateVersion7(), ApplicationId = app.Id, Level = 2, Kind = "declared_by_you", Detail = "O app clicou em enviar; você confirmou que chegou.", At = now });
            foreach (var i in await db.Intents.Where(i => i.ApplicationId == app.Id && i.State == IntentState.SentUnverified).ToListAsync(ct)) i.Close(IntentState.Confirmed, "Você confirmou que chegou.", now);
            return true;
        }
        if (answer != FormQuestions.Arrival[1] || await db.WorkItems.AnyAsync(w => w.ApplicationId == app.Id && w.Step == WorkStep.Prepare && w.CompletedAt == null, ct)) return false;
        await db.ResolveTasksAsync(app.Id, "Você disse que não chegou", now, ct);
        foreach (var i in await db.Intents.Where(i => i.ApplicationId == app.Id && i.State == IntentState.SentUnverified).ToListAsync(ct)) i.Close(IntentState.Discarded, "Você disse que não chegou.", now);
        app.Move(ApplicationState.FormPreparing, "Você disse que não chegou: o formulário é preenchido de novo, com o modo e os limites atuais.", "Você", now);
        db.WorkItems.Add(WorkItem.Create(app.Id, WorkStep.Prepare, now, now));
        return true;
    }
}

internal sealed class FormQuestionEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/applications/questions").WithTags("Applications");
        g.MapGet("", (ApplicationsDbContext db, CancellationToken ct) => new FormQuestions(db).PendingAsync(ct));
        g.MapPost("answers", async (SaveFormAnswersCommand body, ISender sender, CancellationToken ct) =>
            (await sender.Send(body, ct)).ToHttp(r => Results.Ok(r)));
    }
}
