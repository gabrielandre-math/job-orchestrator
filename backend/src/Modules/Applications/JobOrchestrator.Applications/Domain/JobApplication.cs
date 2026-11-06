namespace JobOrchestrator.Applications.Domain;

/// <summary>Evento de domínio: a candidatura mudou de estado. Vira evento de integração na outbox.</summary>
internal sealed record ApplicationMoved(Guid ApplicationId, Guid CampaignId, ApplicationState From, ApplicationState To, DateTime At) : IDomainEvent;

/// <summary>
/// Agregado: uma candidatura. Toda mudança passa por Move, que confere o grafo e grava o histórico (append-only).
/// Confirmação exige evidência forte (nível 3 ou mais).
/// </summary>
internal sealed class JobApplication : AggregateRoot
{
    private readonly List<ApplicationEvent> _history = [];
    private JobApplication() { }
    public Guid Id { get; private set; }
    public Guid CorrelationId { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid JobId { get; private set; }
    public string CanonicalKey { get; private set; } = "";
    public string Company { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Location { get; private set; } = "";
    public string ApplyUrl { get; private set; } = "";
    public int Score { get; private set; }
    /// <summary>"ats": formulário pelo navegador; "manual", "email" ou "link": vai pelo copiloto, com o kit pronto.</summary>
    public string ApplyChannel { get; private set; } = "ats";
    public ApplicationState State { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public string? ResumePath { get; private set; }
    public string? ResumeHash { get; private set; }
    public string? PreparedBy { get; private set; }
    public string? PayloadHash { get; private set; }
    public string PreparedFields { get; private set; } = "[]";
    public string? ReceiptId { get; private set; }
    public int EvidenceLevel { get; private set; }
    public string? LastReason { get; private set; }
    /// <summary>O que veio depois do envio, marcado por você: Entrevista, Oferta, Aceita ou Recusada. Não mexe no estado da máquina.</summary>
    public string? Outcome { get; private set; }
    public DateTime? OutcomeAt { get; private set; }

    public static readonly string[] Outcomes = ["Entrevista", "Oferta", "Aceita", "Recusada"];

    /// <summary>Quando você arquivou: some das telas e das contagens; o histórico fica, com a linha do arquivamento.</summary>
    public DateTime? ArchivedAt { get; private set; }

    /// <summary>Arquivar só vale para candidatura encerrada (a em andamento se retira antes). O histórico só ganha linha, nunca perde.</summary>
    public void Archive(DateTime now)
    {
        if (State is not (ApplicationState.Withdrawn or ApplicationState.SubmittedConfirmed or ApplicationState.SubmittedDeclared or ApplicationState.Failed
            or ApplicationState.DeadLetter or ApplicationState.BlockedByPolicy))
            throw new DomainRuleException("Só dá para arquivar candidatura encerrada: retire antes a que está em andamento.");
        if (ArchivedAt is not null) return;
        _history.Add(ApplicationEvent.Archived(Id, State.ToString(), now));
        ArchivedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Você marca a etapa depois do envio (o quadro de candidaturas); vazio volta para "Enviada". Fica no histórico.</summary>
    public void SetOutcome(string? outcome, DateTime now)
    {
        if (State is not (ApplicationState.SubmittedConfirmed or ApplicationState.SubmittedDeclared))
            throw new DomainRuleException("Só dá para marcar entrevista, oferta ou resposta numa candidatura enviada.");
        if (outcome is not null && !Outcomes.Contains(outcome)) throw new DomainRuleException($"Etapa desconhecida: {outcome}.");
        if (outcome == Outcome) return;
        _history.Add(ApplicationEvent.Step(Id, Outcome ?? "Enviada", outcome ?? "Enviada", now));
        Outcome = outcome;
        OutcomeAt = outcome is null ? null : now;
        UpdatedAt = now;
    }
    public uint Version { get; private set; }
    public IReadOnlyList<ApplicationEvent> History => _history;

    public static JobApplication Create(JobShortlistedV1 job, DateTime now)
    {
        var app = new JobApplication
        {
            Id = Guid.CreateVersion7(), CorrelationId = Guid.CreateVersion7(), CampaignId = job.CampaignId, JobId = job.JobId,
            CanonicalKey = job.CanonicalKey, Company = job.Company, Title = job.Title, Location = job.Location, ApplyUrl = job.ApplyUrl,
            Score = job.Score, ApplyChannel = job.ApplyChannel, State = ApplicationState.Shortlisted, CreatedAt = now, UpdatedAt = now
        };
        app._history.Add(ApplicationEvent.Create(app.Id, null, ApplicationState.Shortlisted, $"Entrou na fila com nota {job.Score}.", "Analista", null, now));
        return app;
    }

    public void Move(ApplicationState to, string reason, string actor, DateTime now, object? detail = null)
    {
        if (!Transitions.Allowed(State, to)) throw new DomainRuleException($"Transição proibida: {State} → {to}.");
        var from = State;
        State = to;
        UpdatedAt = now;
        LastReason = reason;
        _history.Add(ApplicationEvent.Create(Id, from, to, reason, actor, detail is null ? null : JsonSerializer.Serialize(detail, JsonSerializerOptions.Web), now));
        Register(new ApplicationMoved(Id, CampaignId, from, to, now));
    }

    public void MaterialsReady(string resumePath, string resumeHash, DateTime now, string reason = "Currículo gerado a partir do perfil.", object? data = null)
    {
        ResumePath = resumePath;
        ResumeHash = resumeHash;
        Move(ApplicationState.MaterialsReady, reason, "Redator", now, data ?? new { resumeHash });
    }

    public void Prepared(PreparedForm form, IReadOnlyList<RoutingStep> route, DateTime now)
    {
        PreparedBy = form.AdapterId;
        PayloadHash = form.PayloadHash;
        PreparedFields = JsonSerializer.Serialize(form.Fields, JsonSerializerOptions.Web);
        Move(ApplicationState.FormValidated, $"Formulário preenchido e conferido por {form.AdapterId}. Nada foi enviado.", "Preparador", now, new { form.PayloadHash, route });
    }

    /// <summary>Automático: a autorização ativa aprova o payload exato que o adapter acabou de preparar.</summary>
    public void AutoApprove(Guid authorizationId, DateTime now)
    {
        if (PayloadHash is null) throw new DomainRuleException("Sem formulário preparado não há o que aprovar.");
        Move(ApplicationState.ReadyToSubmit, "Aprovada pela autorização de envio automático.", "Guardião", now, new { authorizationId, PayloadHash });
    }

    /// <summary>Aprovação vale só para o payload exato que você viu.</summary>
    public void Approve(string payloadHash, DateTime now)
    {
        if (PayloadHash is null || payloadHash != PayloadHash) throw new DomainRuleException("A aprovação não corresponde ao formulário preparado.");
        Move(ApplicationState.ReadyToSubmit, "Aprovada por você.", "Você", now, new { payloadHash });
    }

    /// <summary>Pronta, mas o teto do dia já foi: continua pronta e o envio fica para depois. O histórico ganha a linha com o motivo.</summary>
    public void Postpone(string reason, string actor, DateTime now)
    {
        if (State != ApplicationState.ReadyToSubmit) throw new DomainRuleException("Só dá para adiar o envio de candidatura pronta para enviar.");
        LastReason = reason;
        UpdatedAt = now;
        _history.Add(ApplicationEvent.Create(Id, State, State, reason, actor, null, now));
    }

    public void Confirmed(SubmissionReceipt receipt, string actor, DateTime now)
    {
        if (receipt.EvidenceLevel < 3) throw new DomainRuleException("Confirmação exige página de confirmação, recibo ou e-mail casado.");
        ReceiptId = string.IsNullOrEmpty(receipt.ReceiptId) ? null : receipt.ReceiptId;
        EvidenceLevel = receipt.EvidenceLevel;
        Move(ApplicationState.SubmittedConfirmed, receipt.Evidence, actor, now, new { receipt.ReceiptId, receipt.EvidenceLevel });
    }
}

/// <summary>Linha do histórico. Só cresce: o banco recusa update e delete.</summary>
internal sealed class ApplicationEvent
{
    private ApplicationEvent() { }
    public long Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public DateTime At { get; private set; }
    public string? From { get; private set; }
    public string To { get; private set; } = "";
    public string Reason { get; private set; } = "";
    public string Actor { get; private set; } = "";
    public string? Detail { get; private set; }

    internal static ApplicationEvent Create(Guid applicationId, ApplicationState? from, ApplicationState to, string reason, string actor, string? detail, DateTime now) =>
        new() { ApplicationId = applicationId, At = now, From = from?.ToString(), To = to.ToString(), Reason = reason, Actor = actor, Detail = detail };

    internal static ApplicationEvent Archived(Guid applicationId, string from, DateTime now) =>
        new() { ApplicationId = applicationId, At = now, From = from, To = "Arquivada", Reason = "Você arquivou: ela sai das telas e das contagens, e o histórico fica.", Actor = "Você" };

    /// <summary>Uma etapa depois do envio, marcada por você no quadro.</summary>
    internal static ApplicationEvent Step(Guid applicationId, string from, string to, DateTime now) =>
        new() { ApplicationId = applicationId, At = now, From = from, To = to, Reason = $"Você moveu de {from} para {to}.", Actor = "Você" };
}

/// <summary>Item da fila de trabalho, com lease para não rodar em dois lugares ao mesmo tempo.</summary>
internal sealed class WorkItem
{
    private WorkItem() { }
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public WorkStep Step { get; private set; }
    public DateTime RunAfter { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresAt { get; private set; }
    public int Attempts { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? LastError { get; private set; }

    public static WorkItem Create(Guid applicationId, WorkStep step, DateTime runAfter, DateTime now) =>
        new() { Id = Guid.CreateVersion7(), ApplicationId = applicationId, Step = step, RunAfter = runAfter, CreatedAt = now, MaxAttempts = step == WorkStep.Submit ? 1 : 4 };

    public void Complete(DateTime now) { CompletedAt = now; LeaseOwner = null; LeaseExpiresAt = null; }
}

/// <summary>Intenção de envio, gravada antes do clique. No máximo uma aberta por candidatura (índice único parcial).</summary>
internal sealed class SubmissionIntent
{
    private SubmissionIntent() { }
    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string PayloadHash { get; private set; } = "";
    public string AdapterId { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }
    public IntentState State { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public string? Note { get; private set; }

    public static SubmissionIntent Open(Guid applicationId, string payloadHash, string adapterId, DateTime now) =>
        new() { Id = Guid.CreateVersion7(), ApplicationId = applicationId, PayloadHash = payloadHash, AdapterId = adapterId, CreatedAt = now, State = IntentState.Open };

    public void Close(IntentState state, string note, DateTime now)
    {
        State = state;
        Note = note;
        if (state is IntentState.Confirmed or IntentState.Discarded) ClosedAt = now;
    }
}

/// <summary>Prova guardada de uma etapa, com o nível de evidência.</summary>
internal sealed class Evidence
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public int Level { get; set; }
    public string Kind { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime At { get; set; }
}

/// <summary>Algo que só você resolve: aprovação, CAPTCHA, login, pergunta nova, reconciliação.</summary>
internal sealed class HumanTask
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public HumanTaskKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? Resolution { get; set; }
}

/// <summary>Sua decisão sobre o payload exato de uma candidatura.</summary>
internal sealed class ApprovalDecision
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string PayloadHash { get; set; } = "";
    public bool Approved { get; set; }
    public DateTime At { get; set; }
    /// <summary>Aprovação feita pela autorização de envio automático, não por você neste formulário.</summary>
    public bool ByAuthorization { get; set; }
    public Guid? AuthorizationId { get; set; }
}
