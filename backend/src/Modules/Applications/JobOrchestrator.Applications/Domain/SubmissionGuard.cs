namespace JobOrchestrator.Applications.Domain;

/// <summary>O que o Guardião olha antes de liberar um envio.</summary>
internal sealed record GuardInput(
    SubmissionMode Mode, ApplicationState State, string? PayloadHash, ApprovalDecision? Approval,
    int ConfirmedToday, int MaxPerDay, bool OpenIntent, int PendingTasks);

/// <summary>Decisão do Guardião, com código estável e motivo em português.</summary>
internal sealed record GuardDecision(bool Allowed, string Code, string Reason);

/// <summary>
/// Guardião (SubmissionGuard): determinístico, sem IA, único que libera envio. A ordem das checagens é fixa:
/// modo e autorização vêm antes de qualquer outra coisa.
/// </summary>
internal static class SubmissionGuard
{
    /// <summary>Depois do preparo: para, pede aprovação ou bloqueia, conforme o modo.</summary>
    public static (ApplicationState Next, string Reason) AfterPrepare(SubmissionMode mode) => mode switch
    {
        SubmissionMode.ReviewRequired => (ApplicationState.AwaitingApproval, "Esperando a sua aprovação do formulário exato."),
        SubmissionMode.AutoSubmit => (ApplicationState.BlockedByPolicy, "Envio automático exige autorização ativa no painel."),
        _ => (ApplicationState.FormValidated, "Modo só preparar: fica pronta, nada é enviado.")
    };

    public static GuardDecision CanSubmit(GuardInput i)
    {
        if (i.Mode is SubmissionMode.DiscoveryOnly or SubmissionMode.PrepareOnly)
            return new(false, "mode-forbids", "A campanha está em um modo que não envia.");
        if (i.Mode == SubmissionMode.AutoSubmit)
            return new(false, "consent-required", "Envio automático exige autorização ativa no painel.");
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
