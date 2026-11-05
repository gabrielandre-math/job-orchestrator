using System.Security.Cryptography;

namespace JobOrchestrator.Applications.Features.Workflow;

/// <summary>Uma etapa da fila. Recebe o id do item e grava o próprio resultado na mesma transação do estado.</summary>
internal interface IStep
{
    WorkStep Step { get; }
    Task RunAsync(Claimed item, CancellationToken ct);
}

/// <summary>O que as etapas compartilham: banco, automação, campanha, perfil e relógio.</summary>
internal sealed class StepKit(ApplicationsDbContext db, IAutomationApi automation, ICampaignsApi campaigns, IProfileApi profile, IMaterialsApi materials, Bundles bundles, TimeProvider time, IConfiguration config)
{
    public ApplicationsDbContext Db => db;
    public IAutomationApi Automation => automation;
    public IProfileApi Profile => profile;
    public IMaterialsApi Materials => materials;
    public Bundles Bundles => bundles;
    public DateTime Now => time.GetUtcNow().UtcDateTime;
    public TimeSpan ReconcileDelay => TimeSpan.FromSeconds(config.GetValue("Workflow:ReconcileDelaySeconds", 20));
    public TimeSpan ReconcileRetry => TimeSpan.FromSeconds(config.GetValue("Workflow:ReconcileRetrySeconds", 30));

    public async Task<(JobApplication App, WorkItem Item)> LoadAsync(Claimed c, CancellationToken ct) =>
        (await db.Applications.FirstAsync(a => a.Id == c.ApplicationId, ct), await db.WorkItems.FirstAsync(w => w.Id == c.Id, ct));

    public async Task<CampaignSettings> CampaignAsync(Guid id, CancellationToken ct) =>
        await campaigns.GetAsync(id, ct) ?? throw new InvalidOperationException("Campanha não encontrada.");

    public void Enqueue(Guid applicationId, WorkStep step, TimeSpan delay) => db.WorkItems.Add(WorkItem.Create(applicationId, step, Now.Add(delay), Now));

    /// <summary>Perfil de teste nunca vai para vaga real: só para os hosts do ATS de teste.</summary>
    public bool IsTestHost(string url) =>
        (config.GetSection("Automation:FakeAtsHosts").Get<string[]>() ?? []).Contains(new Uri(url).Authority, StringComparer.OrdinalIgnoreCase);

    public static HumanTaskKind TaskFor(string? code) => code switch
    {
        "captcha" => HumanTaskKind.Captcha,
        "login" => HumanTaskKind.Login,
        _ => HumanTaskKind.Question
    };
}

/// <summary>
/// Agente Redator: pede ao módulo Materials o currículo da vaga (Seletor, Redator, Conferente, Revisor, Renderizador e Inspetor ATS).
/// Sem perfil confirmado, nada sai; perfil sintético só vai para o ATS de teste.
/// </summary>
internal sealed class MaterialsStep(StepKit k) : IStep
{
    public WorkStep Step => WorkStep.Materials;

    public async Task RunAsync(Claimed c, CancellationToken ct)
    {
        var (app, item) = await k.LoadAsync(c, ct);
        if (app.State is not (ApplicationState.Shortlisted or ApplicationState.RetryScheduled or ApplicationState.PausedProvider))
        {
            item.Complete(k.Now);
            await k.Db.SaveChangesAsync(ct);
            return;
        }
        var profile = await k.Profile.CurrentAsync(ct);
        if (profile is null)
        {
            app.Move(ApplicationState.NeedsHuman, "Sem perfil. Envie o seu currículo na tela Perfil.", "Guardião", k.Now);
            k.Db.AddTask(app.Id, HumanTaskKind.Review, "Enviar currículo", "O perfil ainda não existe.", k.Now);
        }
        else if (profile.Synthetic && !k.IsTestHost(app.ApplyUrl))
        {
            app.Move(ApplicationState.NeedsHuman, "O perfil carregado é de teste e não vai para vaga real. Envie o seu currículo.", "Guardião", k.Now);
            k.Db.AddTask(app.Id, HumanTaskKind.Review, "Enviar currículo", "O perfil atual é sintético.", k.Now);
        }
        else if (!profile.Confirmed && !profile.Synthetic)
        {
            app.Move(ApplicationState.NeedsHuman, "O perfil ainda não foi confirmado na tela única.", "Guardião", k.Now);
            k.Db.AddTask(app.Id, HumanTaskKind.Review, "Confirmar o perfil", "Uma vez só: autorização, pisos, modalidade e o que o modelo inferiu.", k.Now);
        }
        else
        {
            var result = await k.Materials.ComposeResumeAsync(new ComposeRequest(app.Id, app.JobId, app.Company, app.Title), ct);
            if (result.Ok)
            {
                var how = result.Mode switch
                {
                    "ai" => result.Reviewed ? "escrito pela IA, revisado por outra família e conferido fato a fato" : "escrito pela IA e conferido fato a fato, sem revisão cruzada",
                    "local" => "rascunho do modelo local, conferido fato a fato",
                    _ => "só com os fatos do perfil, sem IA"
                };
                app.MaterialsReady(result.PdfPath!, result.Hash!, k.Now, $"Currículo pronto: {how}.",
                    new { resumeHash = result.Hash, result.Mode, result.Reviewed, result.Language, result.VersionId, result.ThreadId });
                if (app.ApplyChannel == "ats") k.Enqueue(app.Id, WorkStep.Prepare, TimeSpan.Zero);
                else
                {
                    // LinkedIn, Gupy, e-mail ou link: nenhum robô envia; o kit fica pronto para você (copiloto).
                    app.Move(ApplicationState.ManualHandoff, "A candidatura vai pelo site original. O currículo está pronto para você enviar.", "Supervisor", k.Now, new { app.ApplyChannel });
                    k.Db.AddTask(app.Id, HumanTaskKind.Review, "Candidatar-se pelo site da vaga", $"{app.Title}, {app.Company}", k.Now);
                }
            }
            else if (result.Paused)
            {
                if (app.State != ApplicationState.PausedProvider)
                    app.Move(ApplicationState.PausedProvider, "Nenhum provedor de IA disponível agora. Tenta de novo em uma hora.", "Redator", k.Now, new { result.ThreadId });
                k.Enqueue(app.Id, WorkStep.Materials, TimeSpan.FromHours(1));
            }
            else
            {
                app.Move(ApplicationState.NeedsHuman, $"O currículo não ficou pronto: {result.Error}", "Redator", k.Now, new { result.ThreadId });
                k.Db.AddTask(app.Id, HumanTaskKind.Review, "Currículo não ficou pronto", result.Error ?? "", k.Now);
            }
        }
        item.Complete(k.Now);
        await k.Db.SaveChangesAsync(ct);
    }
}

/// <summary>Agente Preparador: preenche pelo roteador (com fallback) e entrega ao Guardião. Nunca envia.</summary>
internal sealed class PrepareStep(StepKit k) : IStep
{
    public WorkStep Step => WorkStep.Prepare;

    public async Task RunAsync(Claimed c, CancellationToken ct)
    {
        var (app, item) = await k.LoadAsync(c, ct);
        if (app.State is ApplicationState.MaterialsReady or ApplicationState.RetryScheduled or ApplicationState.NeedsHuman)
        {
            app.Move(ApplicationState.FormPreparing, "Preenchendo o formulário.", "Preparador", k.Now);
            await k.Db.SaveChangesAsync(ct);
        }
        else if (app.State != ApplicationState.FormPreparing)
        {
            item.Complete(k.Now);
            await k.Db.SaveChangesAsync(ct);
            return;
        }

        var campaign = await k.CampaignAsync(app.CampaignId, ct);
        var bundle = await k.Bundles.ForAsync(app, ct) ?? throw new InvalidOperationException("Perfil sumiu entre as etapas.");
        var outcome = await k.Automation.PrepareAsync(new ApplyTarget(app.Id, app.ApplyUrl), bundle, ct);
        var now = k.Now;
        switch (outcome.Kind)
        {
            case AdapterResultKind.Completed:
                app.Prepared(outcome.Value!, outcome.Route, now);
                var (next, reason) = SubmissionGuard.AfterPrepare(campaign.Mode, campaign.Authorization, now, app.Score, outcome.Value!.AdapterId);
                if (next == ApplicationState.ReadyToSubmit)
                {
                    // Automático autorizado: a aprovação fica registrada como da autorização, para o payload exato preparado agora.
                    k.Db.Approvals.Add(new ApprovalDecision { Id = Guid.CreateVersion7(), ApplicationId = app.Id, PayloadHash = app.PayloadHash!, Approved = true, At = now, ByAuthorization = true, AuthorizationId = campaign.Authorization!.Id });
                    app.AutoApprove(campaign.Authorization.Id, now);
                    k.Enqueue(app.Id, WorkStep.Submit, TimeSpan.Zero);
                }
                else if (next != ApplicationState.FormValidated) app.Move(next, reason, "Guardião", now);
                if (next == ApplicationState.AwaitingApproval) k.Db.AddTask(app.Id, HumanTaskKind.Approval, "Aprovar envio", $"{app.Title}, {app.Company}", now);
                break;
            case AdapterResultKind.NeedsHuman or AdapterResultKind.Unsupported:
                app.Move(ApplicationState.NeedsHuman, outcome.Detail, "Preparador", now, new { outcome.ReasonCode, outcome.Route });
                k.Db.AddTask(app.Id, StepKit.TaskFor(outcome.ReasonCode), outcome.ReasonCode == "captcha" ? "Resolver CAPTCHA" : outcome.ReasonCode == "login" ? "Entrar no site" : "Responder pergunta nova", outcome.Detail, now);
                break;
            case AdapterResultKind.PermanentFailure:
                app.Move(ApplicationState.Failed, outcome.Detail, "Preparador", now, new { outcome.ReasonCode, outcome.Route });
                break;
            case AdapterResultKind.BlockedByPolicy:
                app.Move(ApplicationState.BlockedByPolicy, outcome.Detail, "Preparador", now, new { outcome.ReasonCode });
                break;
            default:
                throw new RetryLaterException(TimeSpan.FromSeconds(30), outcome.Detail);
        }
        item.Complete(now);
        await k.Db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Agente Remetente sob o Guardião. A intenção e o estado Submitting são gravados antes do clique; se o processo cair,
/// a próxima execução encontra Submitting e vai para reconciliação, sem clicar de novo.
/// </summary>
internal sealed class SubmitStep(StepKit k, TokenIssuer tokens) : IStep
{
    public WorkStep Step => WorkStep.Submit;

    public async Task RunAsync(Claimed c, CancellationToken ct)
    {
        var (app, item) = await k.LoadAsync(c, ct);
        var db = k.Db;
        if (app.State == ApplicationState.Submitting)
        {
            var stale = await db.Intents.FirstOrDefaultAsync(i => i.ApplicationId == app.Id && i.State == IntentState.Open, ct);
            stale?.Close(IntentState.SentUnverified, "O processo caiu durante o envio.", k.Now);
            app.Move(ApplicationState.SubmissionUnknown, "O processo caiu durante o envio. Nada será reenviado sem reconciliação.", "Supervisor", k.Now);
            k.Enqueue(app.Id, WorkStep.Reconcile, k.ReconcileDelay);
            item.Complete(k.Now);
            await db.SaveChangesAsync(ct);
            return;
        }
        if (app.State != ApplicationState.ReadyToSubmit)
        {
            item.Complete(k.Now);
            await db.SaveChangesAsync(ct);
            return;
        }

        var campaign = await k.CampaignAsync(app.CampaignId, ct);
        var today = k.Now.Date;
        var input = new GuardInput(
            campaign.Mode, app.State, app.PayloadHash,
            await db.Approvals.Where(a => a.ApplicationId == app.Id).OrderByDescending(a => a.At).FirstOrDefaultAsync(ct),
            // Teto do dia conta o que já saiu e o que está saindo ou incerto: envio incerto pode ter chegado.
            await db.Applications.CountAsync(a => a.CampaignId == app.CampaignId && a.Id != app.Id && (a.State == ApplicationState.SubmittedConfirmed && a.UpdatedAt >= today
                || a.State == ApplicationState.SubmissionUnknown || a.State == ApplicationState.Submitting), ct),
            campaign.MaxSubmissionsPerDay,
            await db.Intents.AnyAsync(i => i.ApplicationId == app.Id && (i.State == IntentState.Open || i.State == IntentState.SentUnverified), ct),
            await db.Tasks.CountAsync(t => t.ApplicationId == app.Id && t.ResolvedAt == null, ct),
            campaign.Authorization, k.Now, app.Score, app.PreparedBy,
            await db.Applications.CountAsync(a => a.Id != app.Id && a.Company == app.Company && a.State == ApplicationState.SubmittedConfirmed && a.UpdatedAt >= k.Now.AddDays(-1), ct));
        var decision = SubmissionGuard.CanSubmit(input);
        if (!decision.Allowed)
        {
            app.Move(ApplicationState.BlockedByPolicy, decision.Reason, "Guardião", k.Now, new { decision.Code });
            item.Complete(k.Now);
            await db.SaveChangesAsync(ct);
            return;
        }

        var intent = SubmissionIntent.Open(app.Id, app.PayloadHash!, app.PreparedBy!, k.Now);
        db.Intents.Add(intent);
        app.Move(ApplicationState.Submitting, "O Guardião liberou. Intenção gravada antes do clique.", "Guardião", k.Now, new { intentId = intent.Id });
        await db.SaveChangesAsync(ct);

        var bundle = await k.Bundles.ForAsync(app, ct) ?? throw new InvalidOperationException("Perfil sumiu entre as etapas.");
        var outcome = await k.Automation.SubmitAsync(new ApplyTarget(app.Id, app.ApplyUrl), bundle, tokens.Issue(intent), app.PreparedBy!, ct);
        var now = k.Now;
        switch (outcome.Kind)
        {
            case AdapterResultKind.Completed:
                app.Confirmed(outcome.Value!, "Verificador", now);
                intent.Close(IntentState.Confirmed, outcome.Value!.Evidence, now);
                db.Evidence.Add(new Evidence { Id = Guid.CreateVersion7(), ApplicationId = app.Id, Level = outcome.Value.EvidenceLevel, Kind = outcome.Value.EvidenceLevel == 4 ? "receipt_id_extracted" : "confirmation_page_observed", Detail = outcome.Value.Evidence, At = now });
                break;
            case AdapterResultKind.SubmissionUnknown:
                intent.Close(IntentState.SentUnverified, outcome.Detail, now);
                app.Move(ApplicationState.SubmissionUnknown, outcome.Detail, "Remetente", now, new { outcome.ReasonCode });
                k.Enqueue(app.Id, WorkStep.Reconcile, k.ReconcileDelay);
                break;
            case AdapterResultKind.PermanentFailure:
                intent.Close(IntentState.Discarded, outcome.Detail, now);
                app.Move(ApplicationState.Failed, outcome.Detail, "Remetente", now, new { outcome.ReasonCode });
                break;
            case AdapterResultKind.BlockedByPolicy:
                intent.Close(IntentState.Discarded, "Nada foi clicado.", now);
                app.Move(ApplicationState.BlockedByPolicy, outcome.Detail, "Remetente", now, new { outcome.ReasonCode });
                break;
            default:
                // O adapter só devolve estes resultados antes do clique: nada foi enviado.
                intent.Close(IntentState.Discarded, "Nada foi clicado.", now);
                app.Move(ApplicationState.NeedsHuman, $"Nada foi enviado: {outcome.Detail}", "Remetente", now, new { outcome.ReasonCode });
                db.AddTask(app.Id, StepKit.TaskFor(outcome.ReasonCode), "Revisar envio", outcome.Detail, now);
                break;
        }
        item.Complete(now);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Agente Verificador: depois de envio incerto, só lê. Três consultas sem achar viram tarefa sua.</summary>
internal sealed class ReconcileStep(StepKit k) : IStep
{
    public WorkStep Step => WorkStep.Reconcile;

    public async Task RunAsync(Claimed c, CancellationToken ct)
    {
        var (app, item) = await k.LoadAsync(c, ct);
        if (app.State != ApplicationState.SubmissionUnknown)
        {
            item.Complete(k.Now);
            await k.Db.SaveChangesAsync(ct);
            return;
        }
        Chaos.Point("during-reconcile");
        var bundle = await k.Bundles.ForAsync(app, ct) ?? throw new InvalidOperationException("Perfil sumiu entre as etapas.");
        var outcome = await k.Automation.ReconcileAsync(new ApplyTarget(app.Id, app.ApplyUrl), bundle, app.PreparedBy!, ct);
        var now = k.Now;
        var intent = await k.Db.Intents.FirstOrDefaultAsync(i => i.ApplicationId == app.Id && i.State == IntentState.SentUnverified, ct);
        if (outcome.Kind == AdapterResultKind.Completed && outcome.Value!.Found)
        {
            var id = outcome.Value.ReceiptId ?? "";
            app.Confirmed(new SubmissionReceipt(id, id.Length > 0 ? 4 : 3, "Reconciliação: o ATS confirma a candidatura."), "Verificador", now);
            intent?.Close(IntentState.Confirmed, "Confirmada na reconciliação.", now);
            k.Db.Evidence.Add(new Evidence { Id = Guid.CreateVersion7(), ApplicationId = app.Id, Level = id.Length > 0 ? 4 : 3, Kind = "reconciled", Detail = outcome.Value.Detail, At = now });
        }
        else if (outcome.Kind == AdapterResultKind.Completed && c.Attempts < 3)
        {
            throw new RetryLaterException(k.ReconcileRetry, "O ATS ainda não mostra a candidatura.");
        }
        else if (outcome.Kind is AdapterResultKind.Completed or AdapterResultKind.NeedsHuman)
        {
            app.Move(ApplicationState.NeedsHuman, outcome.Kind == AdapterResultKind.Completed ? "O ATS não mostra a candidatura depois de 3 consultas. Confira e decida." : outcome.Detail, "Verificador", now);
            k.Db.AddTask(app.Id, HumanTaskKind.Reconcile, "Conferir envio incerto", "Veja no site ou no e-mail se a candidatura chegou.", now);
        }
        else throw new RetryLaterException(k.ReconcileRetry, outcome.Detail);
        item.Complete(now);
        await k.Db.SaveChangesAsync(ct);
    }
}
