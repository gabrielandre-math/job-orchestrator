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
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ApprovalDecision ByAuthorization = new() { PayloadHash = Hash, Approved = true, ByAuthorization = true };
    private static SubmitAuthorizationView Auth(int days = 7, int max = 5, int min = 85, params string[] adapters) => new(Guid.NewGuid(), Now.AddDays(-1), Now.AddDays(days), max, min, adapters);
    private static GuardInput Auto(SubmitAuthorizationView? auth, int score = 90, string adapter = "greenhouse-form", int today = 0, int sameCompany = 0, ApprovalDecision? approval = null) =>
        new(SubmissionMode.AutoSubmit, ApplicationState.ReadyToSubmit, Hash, approval ?? ByAuthorization, today, 15, false, 0, auth, Now, score, adapter, sameCompany);

    [Fact] public void Automatico_sem_autorizacao_nao_envia() => Assert.Equal("consent-required", SubmissionGuard.CanSubmit(Auto(null)).Code);
    [Fact] public void Automatico_com_autorizacao_vencida_nao_envia() => Assert.Equal("consent-required", SubmissionGuard.CanSubmit(Auto(Auth(days: -1))).Code);
    [Fact] public void Automatico_com_autorizacao_valida_envia() => Assert.True(SubmissionGuard.CanSubmit(Auto(Auth())).Allowed);
    [Fact] public void Nota_abaixo_da_autorizacao_nao_envia() => Assert.Equal("score-below-authorization", SubmissionGuard.CanSubmit(Auto(Auth(min: 92), score: 90)).Code);
    [Fact] public void Adapter_fora_da_autorizacao_nao_envia() => Assert.Equal("adapter-not-authorized", SubmissionGuard.CanSubmit(Auto(Auth(adapters: "lever-form"), adapter: "greenhouse-form")).Code);
    [Fact] public void Teto_da_autorizacao_bloqueia() => Assert.Equal("authorization-daily-limit", SubmissionGuard.CanSubmit(Auto(Auth(max: 2), today: 2)).Code);
    [Fact] public void Mesma_empresa_no_dia_bloqueia_o_automatico() => Assert.Equal("company-spacing", SubmissionGuard.CanSubmit(Auto(Auth(), sameCompany: 1)).Code);
    [Fact] public void Aprovacao_sua_vale_mesmo_no_automatico() => Assert.True(SubmissionGuard.CanSubmit(Auto(null, approval: Approved)).Allowed);

    /// <summary>Varredura de combinações: modo que não envia nunca libera, e automático sem autorização válida nunca libera sozinho.</summary>
    [Fact]
    public void Nenhuma_combinacao_libera_o_que_nao_pode()
    {
        var states = Enum.GetValues<ApplicationState>();
        var approvals = new ApprovalDecision?[] { null, Approved, ByAuthorization, new() { PayloadHash = Hash, Approved = false } };
        var auths = new SubmitAuthorizationView?[] { null, Auth(days: -1), Auth() };
        foreach (var state in states)
            foreach (var approval in approvals)
                foreach (var auth in auths)
                    foreach (var today in new[] { 0, 3, 20 })
                    {
                        foreach (var mode in new[] { SubmissionMode.DiscoveryOnly, SubmissionMode.PrepareOnly })
                            Assert.False(SubmissionGuard.CanSubmit(new(mode, state, Hash, approval, today, 15, false, 0, auth, Now, 99, "greenhouse-form")).Allowed);
                        var invalid = auth is null || !auth.ValidAt(Now);
                        var human = approval is { Approved: true, ByAuthorization: false };
                        if (invalid && !human)
                            Assert.False(SubmissionGuard.CanSubmit(new(SubmissionMode.AutoSubmit, state, Hash, approval, today, 15, false, 0, auth, Now, 99, "greenhouse-form")).Allowed);
                    }
    }
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
        Assert.Equal(ApplicationState.AwaitingApproval, SubmissionGuard.AfterPrepare(SubmissionMode.AutoSubmit).Next);
        Assert.Equal(ApplicationState.AwaitingApproval, SubmissionGuard.AfterPrepare(SubmissionMode.AutoSubmit, Auth(days: -1), Now, 90, "greenhouse-form").Next);
        Assert.Equal(ApplicationState.AwaitingApproval, SubmissionGuard.AfterPrepare(SubmissionMode.AutoSubmit, Auth(min: 95), Now, 90, "greenhouse-form").Next);
        Assert.Equal(ApplicationState.ReadyToSubmit, SubmissionGuard.AfterPrepare(SubmissionMode.AutoSubmit, Auth(), Now, 90, "greenhouse-form").Next);
    }
}
