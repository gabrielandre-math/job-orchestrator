using System.Text.RegularExpressions;

namespace JobOrchestrator.Applications.Features;

/// <summary>One question of one form, as it waits for you.</summary>
internal sealed record QuestionItem(string Key, string Label, string Kind, IReadOnlyList<string> Options, IReadOnlyList<Guid> Applications);

/// <summary>
/// One line of the questions window: the same question from several forms, in any language, under a short title, with the companies
/// that ask it and one answer for all of them. Draft is what the AI wrote and the check refused, for you to fix instead of starting
/// from nothing; Note says why it came to you.
/// </summary>
internal sealed record PendingGroup(string Key, string Kind, string Title, string Detail, IReadOnlyList<string> Options, IReadOnlyList<string> Companies,
    IReadOnlyList<Guid> Applications, IReadOnlyList<QuestionItem> Items, string? Draft, string? Note);

internal sealed record SavedFormAnswers(int Resumed, int HandedOff);
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
internal sealed partial class FormQuestions(ApplicationsDbContext db)
{
    private sealed record Stop(IReadOnlyList<FormQuestion>? Questions, Dictionary<string, string>? Titles);
    internal sealed record Waiting(JobApplication Application, IReadOnlyList<FormQuestion> Questions, IReadOnlyDictionary<string, string> Titles);

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
                if (JsonSerializer.Deserialize<Stop>(e.Detail, JsonSerializerOptions.Web) is { Questions.Count: > 0 } stop)
                    waiting.Add(new(app, stop.Questions!, stop.Titles ?? []));
            }
            catch (JsonException) { /* Legacy stops with no structured questions remain visible in Attention. */ }
        }
        return waiting;
    }

    /// <summary>The answers to a send the app clicked but could not confirm: only you know if it arrived.</summary>
    internal static readonly string[] Arrival = ["Chegou", "Não chegou, pode enviar de novo", "Ainda não sei"];
    internal static readonly string[] YesNo = ["Sim", "Não"];

    public async Task<IReadOnlyList<PendingGroup>> PendingAsync(CancellationToken ct) =>
        [.. await FormAsync(ct), .. await UncertainAsync(ct)];

    private async Task<IReadOnlyList<PendingGroup>> UncertainAsync(CancellationToken ct) =>
        [.. (await db.Applications.AsNoTracking().Where(a => a.State == ApplicationState.NeedsHuman && a.ArchivedAt == null
                && db.Tasks.Any(t => t.ApplicationId == a.Id && t.ResolvedAt == null && t.Kind == HumanTaskKind.Reconcile)).ToListAsync(ct))
            .Select(a => new PendingGroup($"arrived:{a.Id}", "arrived", $"O envio para a {a.Company} chegou?",
                $"{a.Title}. O app clicou em enviar, mas não viu a confirmação. Confira o e-mail de confirmação ou o portal antes de responder: assim nada vai em dobro.",
                Arrival, [a.Company], [a.Id], [], null, null))];

    private async Task<IReadOnlyList<PendingGroup>> FormAsync(CancellationToken ct)
    {
        var rows = (await WaitingAsync(ct)).SelectMany(w => w.Questions.Select(q => new Row(w.Application, q, w.Titles.GetValueOrDefault(CustomAnswer.Normalize(q.Label))))).ToList();
        if (rows.Count == 0) return [];
        var keys = rows.Select(r => CustomAnswer.Normalize(r.Question.Label)).Distinct().ToList();
        var refused = (await db.FormAnswers.AsNoTracking().Where(f => !f.Accepted && keys.Contains(f.QuestionKey)).OrderByDescending(f => f.CreatedAt)
            .Select(f => new Refusal(f.QuestionKey, f.Answer, f.Note)).ToListAsync(ct)).DistinctBy(f => f.Key).ToDictionary(f => f.Key);
        return [.. rows.GroupBy(r => GroupKey(r.Question, r.Title)).Select(g => Build(g.Key, [.. g], refused)).OrderBy(g => Order(g.Kind)).ThenBy(g => g.Title, StringComparer.Ordinal)];
    }

    private sealed record Row(JobApplication Application, FormQuestion Question, string? Title);
    private sealed record Refusal(string Key, string Answer, string? Note);

    /// <summary>
    /// Questions that mean the same land in one line: every declaration (privacy, terms, data processing) is one consent, your birth date
    /// and current pay are one each, and the rest go by the title the AI gave them, so "Are you willing to travel?" and "Tem
    /// disponibilidade para viajar?" are one yes or no. Choices only join when they offer the same options.
    /// </summary>
    internal static string GroupKey(FormQuestion q, string? title)
    {
        if (q.Kind == "declaration") return "consent";
        if (PersonalAnswer.IsBirthDate(q.Label)) return "birth_date";
        if (CurrentPay().IsMatch(CustomAnswer.Normalize(q.Label))) return "current_pay";
        var topic = CustomAnswer.Normalize(title ?? q.Label);
        if (topic.Length > 120) topic = topic[..120];
        if (IsYesNo(q)) return $"yesno:{topic}";
        var options = string.Join('|', q.Options.Select(CustomAnswer.Normalize));
        return q.Kind switch
        {
            "select" or "radio" => $"choice:{topic}:{options}",
            "checkboxes" => $"boxes:{topic}:{options}",
            "number" or "date" => $"{q.Kind}:{topic}",
            _ => $"text:{topic}",
        };
    }

    /// <summary>A statement to confirm, or a choice between exactly a yes and a no in any language.</summary>
    internal static bool IsYesNo(FormQuestion q) =>
        q.Kind == "confirm" || q.Kind is "radio" or "select" && q.Options.Count == 2
            && q.Options.Any(o => Yes().IsMatch(CustomAnswer.Normalize(o))) && q.Options.Any(o => No().IsMatch(CustomAnswer.Normalize(o)));

    private static PendingGroup Build(string key, IReadOnlyList<Row> rows, IReadOnlyDictionary<string, Refusal> refused)
    {
        var first = rows[0].Question;
        var apps = rows.Select(r => r.Application.Id).Distinct().ToList();
        var companies = rows.Select(r => r.Application.Company).Distinct().ToList();
        var items = rows.GroupBy(r => CustomAnswer.Key(r.Question.Label))
            .Select(g => new QuestionItem(g.Key, g.First().Question.Label, g.First().Question.Kind, g.First().Question.Options, [.. g.Select(r => r.Application.Id).Distinct()])).ToList();
        var who = Join(companies);
        switch (key)
        {
            case "consent":
                return new(key, "consent", apps.Count == 1 ? "1 vaga pede o seu aceite dos termos e da política de privacidade" : $"{apps.Count} vagas pedem o seu aceite dos termos e da política de privacidade",
                    $"Sem o aceite, o formulário não sai. {who}.", YesNo, companies, apps, items, null, null);
            case "birth_date":
                return new(key, "date", "Data de nascimento", $"Pedida por {who}. Usada só quando um formulário pede; não aparece no currículo.", [], companies, apps, items, null, null);
            case "current_pay":
                return new(key, "text", "Salário atual", $"Pedido por {who}. O seu piso não responde a isso: é o que você ganha hoje, com a moeda e o vínculo.", [], companies, apps, items, null, null);
        }
        var title = rows.Select(r => r.Title).FirstOrDefault(t => t is not null) ?? Cut(first.Label, 140);
        var kind = key.StartsWith("yesno:", StringComparison.Ordinal) ? "yesno" : key.StartsWith("choice:", StringComparison.Ordinal) ? "choice"
            : key.StartsWith("boxes:", StringComparison.Ordinal) ? "checkboxes" : first.Kind is "number" or "date" ? first.Kind
            : rows.Any(r => r.Question.Kind == "textarea") ? "textarea" : "text";
        var options = kind == "yesno" ? YesNo : kind is "choice" or "checkboxes" ? first.Options : [];
        var refusal = items.Select(i => refused.GetValueOrDefault(CustomAnswer.Normalize(i.Label))).FirstOrDefault(r => r is not null);
        var draft = kind is "text" or "textarea" && refusal is { Answer.Length: >= 20 } && refusal.Note != "a resposta não responde à pergunta" ? refusal.Answer : null;
        return new(key, kind, title, who, options, companies, apps, items, draft, Why(refusal?.Note, draft is not null));
    }

    /// <summary>Why the question came to you, in words for you.</summary>
    private static string? Why(string? note, bool draft) => note switch
    {
        null => null,
        "o seu perfil não fala disso" => "O seu currículo e o seu perfil não falam disso. A resposta é sua.",
        _ when note.StartsWith("fora dos fatos:", StringComparison.Ordinal) && draft => $"A IA escreveu um rascunho, mas ele cita o que não está no seu perfil ({note["fora dos fatos:".Length..].Trim()}). Confira antes de usar.",
        "escrita em outra língua" when draft => "A IA escreveu na língua errada. Ajuste o rascunho ou escreva a sua.",
        "curta demais para uma pergunta aberta" when draft => "O rascunho da IA ficou curto. Complete com a sua história.",
        _ => "Nenhum modelo respondeu isso pelos fatos do seu perfil. A resposta é sua.",
    };

    private static int Order(string kind) => kind switch
    {
        "consent" => 0, "date" => 1, "yesno" => 2, "choice" or "checkboxes" => 3, "number" => 4, "text" => 5, "textarea" => 6, _ => 7,
    };

    private static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        <= 4 => $"{string.Join(", ", names.Take(names.Count - 1))} e {names[^1]}",
        _ => $"{string.Join(", ", names.Take(3))} e mais {names.Count - 3}",
    };

    private static string Cut(string text, int max) => text.Length <= max ? text.Trim() : text[..max].TrimEnd() + "…";

    /// <summary>
    /// Your one answer for a line, written the way each form takes it: a yes or a no becomes the option that form offers ("Yes", "Sim",
    /// "Sí"); a declaration or statement you did not accept has no value, and its applications go back to you.
    /// </summary>
    internal static string? Value(PendingGroup group, QuestionItem item, string answer)
    {
        var yes = answer == YesNo[0];
        return group.Kind switch
        {
            "consent" => yes ? "Sim" : null,
            "yesno" when item.Kind == "confirm" => yes ? "Sim" : null,
            "yesno" => item.Options.FirstOrDefault(o => (yes ? Yes() : No()).IsMatch(CustomAnswer.Normalize(o))) ?? (yes ? "Sim" : "Não"),
            _ => answer.Trim(),
        };
    }

    public async Task<int> ResumeAsync(IReadOnlySet<string> keys, IReadOnlySet<Guid> skip, DateTime now, CancellationToken ct)
    {
        var count = 0;
        foreach (var w in await WaitingAsync(ct))
        {
            if (skip.Contains(w.Application.Id) || !w.Questions.Any(q => keys.Contains(CustomAnswer.Key(q.Label)))) continue;
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

    /// <summary>What you did not accept leaves the form to you: the application goes the usual way, by you, with the resume ready.</summary>
    public async Task<int> HandOffAsync(IReadOnlySet<Guid> ids, DateTime now, CancellationToken ct)
    {
        if (ids.Count == 0) return 0;
        var apps = await db.Applications.Where(a => ids.Contains(a.Id) && a.State == ApplicationState.NeedsHuman).ToListAsync(ct);
        foreach (var app in apps)
        {
            await db.ResolveTasksAsync(app.Id, "Você não aceitou o que o formulário pede", now, ct);
            app.Move(ApplicationState.ManualHandoff, "Você não aceitou o que o formulário pede, então o app não envia. A candidatura vai pelo site original, se você quiser, com o currículo pronto.", "Você", now);
            db.AddTask(app.Id, HumanTaskKind.Review, "Candidatar-se pelo site da vaga", $"{app.Title}, {app.Company}", now);
        }
        return apps.Count;
    }

    [GeneratedRegex(@"\b(salario|remuneracao|compensation|salary|pay|salario actual|sueldo) (atual|actual|current)\b|\bcurrent (salary|compensation|pay|ctc)\b|\b(salario|remuneracao) (que voce recebe|recebid[oa]) hoje\b")]
    private static partial Regex CurrentPay();
    [GeneratedRegex(@"^(yes|sim|si|s|y|true|verdadeiro)\b")] private static partial Regex Yes();
    [GeneratedRegex(@"^(no|nao|n|false|falso)\b")] private static partial Regex No();
}

internal sealed class SaveFormAnswersHandler(ApplicationsDbContext db, TimeProvider time) : ICommandHandler<SaveFormAnswersCommand, SavedFormAnswers>
{
    public async Task<Result<SavedFormAnswers>> Handle(SaveFormAnswersCommand c, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var questions = new FormQuestions(db);
        var groups = (await questions.PendingAsync(ct)).ToDictionary(g => g.Key, StringComparer.Ordinal);
        var arrivals = new List<AnswerInput>();
        var form = new List<AnswerInput>();
        var declined = new HashSet<Guid>();
        foreach (var a in c.Answers)
        {
            if (a.Key is { } k && k.StartsWith("arrived:", StringComparison.Ordinal)) { arrivals.Add(a); continue; }
            // One answer for a line goes to every form in it, in that form's own words.
            if (a.Key is { } key && groups.TryGetValue(key, out var group))
            {
                if (string.IsNullOrWhiteSpace(a.Answer)) continue;
                foreach (var item in group.Items)
                    if (FormQuestions.Value(group, item, a.Answer) is { } value) form.Add(new AnswerInput(item.Label, value, item.Key));
                    else foreach (var id in item.Applications) declined.Add(id);
                continue;
            }
            form.Add(a);
        }
        AnswerStore.SaveMany(form, now);
        var handedOff = await questions.HandOffAsync(declined, now, ct);
        var keys = form.Where(a => !string.IsNullOrWhiteSpace(a.Answer)).Select(a => CustomAnswer.Key(a.Question)).ToHashSet(StringComparer.Ordinal);
        var resumed = keys.Count == 0 ? 0 : await questions.ResumeAsync(keys, declined, now, ct);
        foreach (var a in arrivals)
            if (Guid.TryParse(a.Key!["arrived:".Length..], out var id) && await ArrivedAsync(id, a.Answer, now, ct)) resumed++;
        return new SavedFormAnswers(resumed, handedOff);
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
