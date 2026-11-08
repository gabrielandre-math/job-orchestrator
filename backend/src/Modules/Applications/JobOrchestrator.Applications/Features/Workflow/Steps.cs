using System.Security.Cryptography;

namespace JobOrchestrator.Applications.Features.Workflow;

/// <summary>Uma etapa da fila. Recebe o id do item e grava o próprio resultado na mesma transação do estado.</summary>
internal interface IStep
{
    WorkStep Step { get; }
    Task RunAsync(Claimed item, CancellationToken ct);
}

/// <summary>O que as etapas compartilham: banco, automação, campanha, perfil e relógio.</summary>
internal sealed class StepKit(ApplicationsDbContext db, IAutomationApi automation, ICampaignsApi campaigns, IProfileApi profile, IMaterialsApi materials, Bundles bundles, TimeProvider time, IConfiguration config, IJobsApi jobs)
{
    public ApplicationsDbContext Db => db;
    public IJobsApi Jobs => jobs;
    public IAutomationApi Automation => automation;
    public IProfileApi Profile => profile;
    public IMaterialsApi Materials => materials;
    public Bundles Bundles => bundles;
    public DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>What the form adapters did, in the trail: the adapter that ran, how it ended, the reason code and the questions it brought.</summary>
    public static void Traced<T>(AutomationOutcome<T> outcome, string what, JobApplication app)
    {
        var ran = outcome.Route.LastOrDefault(r => r.Outcome != "skipped");
        Trail.Shared?.Add("form", outcome.Kind is AdapterResultKind.Completed ? "info" : outcome.Kind is AdapterResultKind.NeedsHuman or AdapterResultKind.Unsupported ? "warn" : "error",
            $"{what} {ran?.AdapterId ?? "sem adapter"}: {outcome.Kind}", outcome.Detail, code: outcome.ReasonCode, data: new Dictionary<string, string?>
            {
                ["application"] = app.Id.ToString(), ["company"] = app.Company, ["url"] = app.ApplyUrl,
                ["questions"] = outcome.Questions is { Count: > 0 } q ? string.Join(" | ", q.Select(x => $"[{x.Kind}] {(x.Label.Length > 90 ? x.Label[..90] + "…" : x.Label)}")) : null,
            });
    }

    public async Task<(JobApplication App, WorkItem Item)> LoadAsync(Claimed c, CancellationToken ct) =>
        (await db.Applications.FirstAsync(a => a.Id == c.ApplicationId, ct), await db.WorkItems.FirstAsync(w => w.Id == c.Id, ct));

    public async Task<CampaignSettings> CampaignAsync(Guid id, CancellationToken ct) =>
        await campaigns.GetAsync(id, ct) ?? throw new InvalidOperationException("Campanha não encontrada.");

    public void Enqueue(Guid applicationId, WorkStep step, TimeSpan delay) => db.WorkItems.Add(WorkItem.Create(applicationId, step, Now.Add(delay), Now));

    /// <summary>Perfil de teste nunca vai para vaga real: só para os hosts do ATS de teste.</summary>
    public bool IsTestHost(string url) =>
        (config.GetSection("Automation:FakeAtsHosts").Get<string[]>() ?? []).Contains(new Uri(url).Authority, StringComparer.OrdinalIgnoreCase);

    /// <summary>A stop that no answer of yours solves: the site itself keeps the robot out or shows no form it can fill.</summary>
    public static bool Technical(string? code) => code is "captcha" or "login" or "mfa" or "blocked" or "no-form" or "no-fields" or "multi-step" or "file"
        or "no-email-field" or "custom-field" or "fill-mismatch" or "no-adapter";

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
                    new { resumeHash = result.Hash, result.Mode, result.Reviewed, result.Language, result.VersionId, result.ThreadId }, vetted: result.Mode == "plain" || result.Reviewed);
                // A link to a site with an open form is tried like a platform; the login and CAPTCHA checks still hand it to you.
                if (app.ApplyChannel == "ats" || app.ApplyChannel == "link" && k.Automation.CapabilityOf(app.ApplyUrl, app.ApplyChannel).Prepare)
                    k.Enqueue(app.Id, WorkStep.Prepare, TimeSpan.Zero);
                else
                {
                    // LinkedIn, Gupy, e-mail ou link de agregador: nenhum robô envia; o kit fica pronto para você (copiloto).
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

/// <summary>Agente Preparador: preenche pelo roteador (com fallback) e entrega a candidatura pronta para você enviar. Nunca envia.</summary>
internal sealed class PrepareStep(StepKit k, Answerer answerer) : IStep
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

        // The privacy and terms boxes are ticked only in the prepare's own browser, which never sends; on the site, you tick them.
        var bundle = await k.Bundles.ForAsync(app, true, ct) ?? throw new InvalidOperationException("Perfil sumiu entre as etapas.");
        var outcome = await k.Automation.PrepareAsync(new ApplyTarget(app.Id, app.ApplyUrl), bundle, ct);
        StepKit.Traced(outcome, "preparo", app);
        var now = k.Now;
        switch (outcome.Kind)
        {
            case AdapterResultKind.Completed:
                // Ready: the form is filled and checked, and the send is yours, with the copilot. The app never clicks send.
                app.Prepared(outcome.Value!, outcome.Route, now);
                app.Move(ApplicationState.ManualHandoff, "Pronta para você enviar. Abra preenchida: o copiloto completa o formulário e anexa o currículo, e o clique em enviar é seu.", "Preparador", now);
                k.Db.AddTask(app.Id, HumanTaskKind.Review, "Candidatar-se pelo site da vaga", $"{app.Title}, {app.Company}", now);
                break;
            case AdapterResultKind.RetryableFailure when outcome.ReasonCode == "circuit-open":
                // A paused adapter (three failures in a row on this site) is back in minutes: the form waits for it instead of asking you.
                throw new RetryLaterException(TimeSpan.FromMinutes(11), outcome.Detail);
            case AdapterResultKind.NeedsHuman or AdapterResultKind.Unsupported:
                // Questions the form asked and nobody answered yet go to the AI with your profile's facts; with answers, the form is filled again.
                if (outcome.Questions is { Count: > 0 } asked && await answerer.AnswerAsync(app, asked, ct) is { Retry: true } round)
                {
                    app.Move(ApplicationState.RetryScheduled, round.Reason, "Respondente", now, new { round.Answered, round.Provider, outcome.Route });
                    k.Enqueue(app.Id, WorkStep.Prepare, round.Delay);
                    break;
                }
                if ((app.ApplyChannel == "link" || StepKit.Technical(outcome.ReasonCode)) && outcome.Questions is null)
                {
                    // What a robot cannot pass (login, CAPTCHA, anti-robot, no open form) sends the application the usual way, by you:
                    // "Precisa de você" stays for what only you can answer.
                    var why = outcome.Detail.Length > 300 ? outcome.Detail[..299] + "…" : outcome.Detail;
                    app.Move(ApplicationState.ManualHandoff, $"A candidatura vai pelo site original. {why} O currículo está pronto para você enviar.", "Preparador", now, new { outcome.ReasonCode, outcome.Route });
                    k.Db.AddTask(app.Id, HumanTaskKind.Review, "Candidatar-se pelo site da vaga", $"{app.Title}, {app.Company}", now);
                    break;
                }
                var detail = outcome.Questions is { Count: > 0 } && await answerer.RefusalsAsync(app.Id, ct) is { } refused ? $"{outcome.Detail} {refused}" : outcome.Detail;
                // Each question that comes to you gets a short title, so the questions window lists what is asked, not the form's text.
                var titles = outcome.Questions is { Count: > 0 } open ? await answerer.TitlesAsync(open, ct) : null;
                app.Move(ApplicationState.NeedsHuman, detail.Length > 500 ? detail[..499] + "…" : detail, "Preparador", now, new { outcome.ReasonCode, outcome.Route, outcome.Questions, Titles = titles });
                k.Db.AddTask(app.Id, StepKit.TaskFor(outcome.ReasonCode), outcome.ReasonCode == "captcha" ? "Resolver CAPTCHA" : outcome.ReasonCode == "login" ? "Entrar no site" : "Responder pergunta nova", detail.Length > 500 ? detail[..499] + "…" : detail, now);
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
