using JobOrchestrator.Jobs.Contracts;

namespace JobOrchestrator.Applications.Domain;

/// <summary>O que o Guardião olha antes de liberar um envio.</summary>
internal sealed record GuardInput(
    SubmissionMode Mode, ApplicationState State, string? PayloadHash, ApprovalDecision? Approval,
    int ConfirmedToday, int MaxPerDay, bool OpenIntent, int PendingTasks,
    SubmitAuthorizationView? Authorization = null, DateTime Now = default, int Score = 0, string? AdapterId = null, int SameCompanyToday = 0, string? Eligibility = null,
    bool ResumeVetted = true);

/// <summary>Decisão do Guardião, com código estável e motivo em português.</summary>
internal sealed record GuardDecision(bool Allowed, string Code, string Reason);

/// <summary>
/// Guardião (SubmissionGuard): determinístico, sem IA, único que libera envio. A ordem das checagens é fixa:
/// modo e autorização vêm antes de qualquer outra coisa.
/// </summary>
internal static class SubmissionGuard
{
    /// <summary>
    /// Depois do preparo: para, pede aprovação ou, no automático com autorização válida para esta vaga, segue sozinho.
    /// Automático sem autorização válida (vencida, revogada, nota abaixo, adapter fora) cai para a sua aprovação: nunca envia sozinho.
    /// </summary>
    public static (ApplicationState Next, string Reason) AfterPrepare(SubmissionMode mode, SubmitAuthorizationView? auth = null, DateTime now = default, int score = 0, string? adapterId = null, string? eligibility = null,
        bool vetted = true) => mode switch
    {
        SubmissionMode.ReviewRequired => (ApplicationState.AwaitingApproval, "Esperando a sua aprovação do formulário exato."),
        SubmissionMode.AutoSubmit when auth is null || !auth.ValidAt(now) => (ApplicationState.AwaitingApproval, "A autorização de envio automático não está ativa: esperando a sua aprovação."),
        SubmissionMode.AutoSubmit when score < auth!.MinScore => (ApplicationState.AwaitingApproval, $"Nota {score} abaixo do mínimo da autorização ({auth.MinScore}): esperando a sua aprovação."),
        SubmissionMode.AutoSubmit when !auth!.Allows(adapterId) => (ApplicationState.AwaitingApproval, $"O adapter {adapterId} não está na autorização: esperando a sua aprovação."),
        SubmissionMode.AutoSubmit when !AutoEligibility.Allows(eligibility) => (ApplicationState.AwaitingApproval, "A vaga não confirma que aceita quem mora no Brasil: no automático, ela espera a sua aprovação."),
        SubmissionMode.AutoSubmit when !vetted => (ApplicationState.AwaitingApproval, Unvetted),
        SubmissionMode.AutoSubmit => (ApplicationState.ReadyToSubmit, "Aprovada pela autorização de envio automático."),
        _ => (ApplicationState.FormValidated, "Modo só preparar: fica pronta, nada é enviado.")
    };

    private const string Unvetted = "O currículo foi escrito por um modelo e a revisão cruzada não conferiu todas as afirmações: esperando a sua aprovação.";

    public static GuardDecision CanSubmit(GuardInput i)
    {
        if (i.Mode is SubmissionMode.DiscoveryOnly or SubmissionMode.PrepareOnly)
            return new(false, "mode-forbids", "A campanha está em um modo que não envia.");
        // Aprovação dada pela autorização só vale com o automático ligado: desligou, a vaga volta a precisar de você.
        if (i.Mode != SubmissionMode.AutoSubmit && i.Approval is { ByAuthorization: true })
            return new(false, "consent-required", "O envio automático foi desligado: esta vaga espera a sua aprovação.");
        if (i.Mode == SubmissionMode.AutoSubmit && i.Approval is not { Approved: true, ByAuthorization: false })
        {
            // Automático: vale só a autorização ativa, dentro do escopo e dos limites dela.
            if (i.Authorization is null || !i.Authorization.ValidAt(i.Now))
                return new(false, "consent-required", "Envio automático exige autorização ativa no painel.");
            if (i.Score < i.Authorization.MinScore)
                return new(false, "score-below-authorization", $"A nota {i.Score} está abaixo do mínimo da autorização ({i.Authorization.MinScore}).");
            if (!i.Authorization.Allows(i.AdapterId))
                return new(false, "adapter-not-authorized", $"O adapter {i.AdapterId} não está na autorização.");
            if (!AutoEligibility.Allows(i.Eligibility))
                return new(false, "eligibility-unconfirmed", "A vaga não confirma que aceita quem mora no Brasil: no automático, ela espera a sua aprovação.");
            if (!i.ResumeVetted)
                return new(false, "resume-unvetted", Unvetted);
            if (i.ConfirmedToday >= i.Authorization.MaxPerDay)
                return new(false, "authorization-daily-limit", $"Teto de {i.Authorization.MaxPerDay} envios por dia da autorização atingido.");
            if (i.SameCompanyToday > 0)
                return new(false, "company-spacing", "Já houve envio automático para esta empresa nas últimas 24 horas.");
        }
        if (i.State != ApplicationState.ReadyToSubmit)
            return new(false, "wrong-state", $"A candidatura está em {i.State}, não em pronta para enviar.");
        if (i.PayloadHash is null || i.Approval is not { Approved: true } || i.Approval.PayloadHash != i.PayloadHash)
            return new(false, "approval-mismatch", "Não há aprovação sua para este formulário exato.");
        if (i.OpenIntent)
            return new(false, "intent-open", "Já existe um envio em andamento para esta candidatura.");
        if (i.PendingTasks > 0)
            return new(false, "pending-tasks", "Há pendências suas abertas nesta candidatura.");
        if (i.ConfirmedToday >= i.MaxPerDay)
            return new(false, "daily-limit", $"Limite diário de {i.MaxPerDay} envios atingido.");
        return new(true, "allowed", "Todas as checagens passaram.");
    }

    /// <summary>Teto do dia e espaço entre envios para a mesma empresa não perdem a vaga: ela continua pronta e espera o dia seguinte.</summary>
    public static bool WaitsForTomorrow(string code) => code is "daily-limit" or "authorization-daily-limit" or "company-spacing";

    /// <summary>Meia-noite de hoje no fuso da campanha, em UTC: o "por dia" vira junto com o seu dia, não às 21h de Brasília.</summary>
    public static DateTime StartOfDay(DateTime utcNow, string? timeZone)
    {
        var zone = timeZone is { Length: > 0 } && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var found) ? found : TimeZoneInfo.Utc;
        var midnight = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone).Date;
        // Fuso que pula a meia-noite no horário de verão: o dia começa na primeira hora que existe.
        if (zone.IsInvalidTime(midnight)) midnight = midnight.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(midnight, zone);
    }
}
