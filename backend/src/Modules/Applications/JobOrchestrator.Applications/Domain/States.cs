namespace JobOrchestrator.Applications.Domain;

/// <summary>Estados de uma candidatura (docs/architecture.md §5).</summary>
internal enum ApplicationState
{
    Shortlisted, MaterialsReady, FormPreparing, FormValidated, AwaitingApproval, ReadyToSubmit, Submitting,
    SubmittedConfirmed, SubmissionUnknown, Failed, NeedsHuman, BlockedByPolicy, PausedProvider, RetryScheduled,
    DeadLetter, Withdrawn, ManualHandoff, SubmittedDeclared
}

/// <summary>
/// O grafo de transições como dado. Não existe aresta de SubmissionUnknown de volta para envio:
/// envio incerto só sai por reconciliação ou por decisão humana.
/// </summary>
internal static class Transitions
{
    private static readonly Dictionary<ApplicationState, ApplicationState[]> Graph = new()
    {
        [ApplicationState.Shortlisted] = [ApplicationState.MaterialsReady, ApplicationState.NeedsHuman, ApplicationState.PausedProvider, ApplicationState.RetryScheduled, ApplicationState.Withdrawn, ApplicationState.DeadLetter],
        [ApplicationState.MaterialsReady] = [ApplicationState.FormPreparing, ApplicationState.ManualHandoff, ApplicationState.Withdrawn],
        [ApplicationState.FormPreparing] = [ApplicationState.FormValidated, ApplicationState.NeedsHuman, ApplicationState.RetryScheduled, ApplicationState.Failed, ApplicationState.BlockedByPolicy, ApplicationState.ManualHandoff, ApplicationState.Withdrawn, ApplicationState.DeadLetter],
        [ApplicationState.FormValidated] = [ApplicationState.AwaitingApproval, ApplicationState.ReadyToSubmit, ApplicationState.BlockedByPolicy, ApplicationState.FormPreparing, ApplicationState.Withdrawn],
        [ApplicationState.AwaitingApproval] = [ApplicationState.ReadyToSubmit, ApplicationState.FormPreparing, ApplicationState.Withdrawn],
        // Pronta pela autorização que acabou volta para a sua aprovação.
        [ApplicationState.ReadyToSubmit] = [ApplicationState.Submitting, ApplicationState.AwaitingApproval, ApplicationState.BlockedByPolicy, ApplicationState.Withdrawn],
        // Saídas de Submitting que não são confirmação nem incerteza exigem prova de que nada foi clicado (o adapter só devolve essas antes do clique).
        [ApplicationState.Submitting] = [ApplicationState.SubmittedConfirmed, ApplicationState.SubmissionUnknown, ApplicationState.Failed, ApplicationState.NeedsHuman, ApplicationState.BlockedByPolicy, ApplicationState.ReadyToSubmit],
        [ApplicationState.SubmissionUnknown] = [ApplicationState.SubmittedConfirmed, ApplicationState.NeedsHuman, ApplicationState.Failed],
        [ApplicationState.ManualHandoff] = [ApplicationState.SubmittedConfirmed, ApplicationState.SubmittedDeclared, ApplicationState.FormPreparing, ApplicationState.Withdrawn],
        [ApplicationState.NeedsHuman] = [ApplicationState.FormPreparing, ApplicationState.MaterialsReady, ApplicationState.SubmittedDeclared, ApplicationState.Failed, ApplicationState.Withdrawn],
        [ApplicationState.PausedProvider] = [ApplicationState.Shortlisted, ApplicationState.MaterialsReady],
        [ApplicationState.RetryScheduled] = [ApplicationState.Shortlisted, ApplicationState.MaterialsReady, ApplicationState.FormPreparing, ApplicationState.DeadLetter],
        [ApplicationState.BlockedByPolicy] = [ApplicationState.FormValidated, ApplicationState.Withdrawn],
        [ApplicationState.SubmittedDeclared] = [ApplicationState.SubmittedConfirmed],
        [ApplicationState.SubmittedConfirmed] = [],
        [ApplicationState.Failed] = [],
        [ApplicationState.Withdrawn] = [],
        [ApplicationState.DeadLetter] = []
    };

    public static bool Allowed(ApplicationState from, ApplicationState to) => Graph.TryGetValue(from, out var next) && next.Contains(to);
    public static IReadOnlyDictionary<ApplicationState, ApplicationState[]> All => Graph;
}

/// <summary>Etapas da fila de trabalho. Só Submit é irreversível.</summary>
internal enum WorkStep { Materials, Prepare, Submit, Reconcile }

internal enum IntentState { Open, SentUnverified, Confirmed, Discarded }

internal enum HumanTaskKind { Approval, Captcha, Login, Question, Reconcile, Review }
