namespace JobOrchestrator.Applications.Domain;

/// <summary>O que o Guardião olha antes de liberar um envio.</summary>
internal sealed record GuardInput(
    SubmissionMode Mode, ApplicationState State, string? PayloadHash, ApprovalDecision? Approval,
    int ConfirmedToday, int MaxPerDay, bool OpenIntent, int PendingTasks,
    SubmitAuthorizationView? Authorization = null, DateTime Now = default, int Score = 0, string? AdapterId = null, int SameCompanyToday = 0);

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
    public static (ApplicationState Next, string Reason) AfterPrepare(SubmissionMode mode, SubmitAuthorizationView? auth = null, DateTime now = default, int score = 0, string? adapterId = null) => mode switch
    {
        SubmissionMode.ReviewRequired => (ApplicationState.AwaitingApproval, "Esperando a sua aprovação do formulário exato."),
        SubmissionMode.AutoSubmit when auth is null || !auth.ValidAt(now) => (ApplicationState.AwaitingApproval, "A autorização de envio automático não está ativa: esperando a sua aprovação."),
        SubmissionMode.AutoSubmit when score < auth!.MinScore => (ApplicationState.AwaitingApproval, $"Nota {score} abaixo do mínimo da autorização ({auth.MinScore}): esperando a sua aprovação."),
        SubmissionMode.AutoSubmit when !auth!.Allows(adapterId) => (ApplicationState.AwaitingApproval, $"O adapter {adapterId} não está na autorização: esperando a sua aprovação."),
        SubmissionMode.AutoSubmit => (ApplicationState.ReadyToSubmit, "Aprovada pela autorização de envio automático."),
        _ => (ApplicationState.FormValidated, "Modo só preparar: fica pronta, nada é enviado.")
    };

    public static GuardDecision CanSubmit(GuardInput i)
    {
        if (i.Mode is SubmissionMode.DiscoveryOnly or SubmissionMode.PrepareOnly)
            return new(false, "mode-forbids", "A campanha está em um modo que não envia.");
        if (i.Mode == SubmissionMode.AutoSubmit && i.Approval is not { Approved: true, ByAuthorization: false })
        {
            // Automático: vale só a autorização ativa, dentro do escopo e dos limites dela.
            if (i.Authorization is null || !i.Authorization.ValidAt(i.Now))
                return new(false, "consent-required", "Envio automático exige autorização ativa no painel.");
            if (i.Score < i.Authorization.MinScore)
                return new(false, "score-below-authorization", $"A nota {i.Score} está abaixo do mínimo da autorização ({i.Authorization.MinScore}).");
            if (!i.Authorization.Allows(i.AdapterId))
                return new(false, "adapter-not-authorized", $"O adapter {i.AdapterId} não está na autorização.");
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
}
