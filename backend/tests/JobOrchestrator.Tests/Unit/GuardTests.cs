namespace JobOrchestrator.Tests.Unit;

/// <summary>O Guardião só libera com modo certo, aprovação do payload exato, sem intenção aberta e dentro do limite.</summary>
public sealed class GuardTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly ApprovalDecision Approved = new() { PayloadHash = Hash, Approved = true };

    private static GuardInput Input(SubmissionMode mode = SubmissionMode.ReviewRequired, ApprovalDecision? approval = null, int today = 0, bool intent = false, int tasks = 0) =>
        new(mode, ApplicationState.ReadyToSubmit, Hash, approval ?? Approved, today, 15, intent, tasks);

    [Fact] public void Libera_o_caminho_feliz() => Assert.True(SubmissionGuard.CanSubmit(Input()).Allowed);
    [Fact] public void So_preparar_nunca_envia() => Assert.Equal("mode-forbids", SubmissionGuard.CanSubmit(Input(SubmissionMode.PrepareOnly)).Code);
    [Fact] public void Automatico_sem_autorizacao_nao_envia() => Assert.Equal("consent-required", SubmissionGuard.CanSubmit(Input(SubmissionMode.AutoSubmit)).Code);
    [Fact] public void Aprovacao_de_outro_payload_nao_vale() => Assert.Equal("approval-mismatch", SubmissionGuard.CanSubmit(Input(approval: new ApprovalDecision { PayloadHash = new string('b', 64), Approved = true })).Code);
    [Fact] public void Recusa_nao_vale_como_aprovacao() => Assert.Equal("approval-mismatch", SubmissionGuard.CanSubmit(Input(approval: new ApprovalDecision { PayloadHash = Hash, Approved = false })).Code);
    [Fact] public void Intencao_aberta_bloqueia() => Assert.Equal("intent-open", SubmissionGuard.CanSubmit(Input(intent: true)).Code);
    [Fact] public void Pendencia_aberta_bloqueia() => Assert.Equal("pending-tasks", SubmissionGuard.CanSubmit(Input(tasks: 1)).Code);
    [Fact] public void Limite_diario_bloqueia() => Assert.Equal("daily-limit", SubmissionGuard.CanSubmit(Input(today: 15)).Code);

    [Fact]
    public void Depois_do_preparo_o_modo_decide()
    {
        Assert.Equal(ApplicationState.FormValidated, SubmissionGuard.AfterPrepare(SubmissionMode.PrepareOnly).Next);
        Assert.Equal(ApplicationState.AwaitingApproval, SubmissionGuard.AfterPrepare(SubmissionMode.ReviewRequired).Next);
        Assert.Equal(ApplicationState.BlockedByPolicy, SubmissionGuard.AfterPrepare(SubmissionMode.AutoSubmit).Next);
    }
}
