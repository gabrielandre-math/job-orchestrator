using JobOrchestrator.BuildingBlocks;
using JobOrchestrator.Jobs.Contracts;

namespace JobOrchestrator.Tests.Unit;

/// <summary>O grafo de estados protege o envio: incerteza nunca volta para envio, e confirmação exige evidência.</summary>
public sealed class TransitionTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Envio_incerto_nunca_volta_para_envio()
    {
        Assert.False(Transitions.Allowed(ApplicationState.SubmissionUnknown, ApplicationState.ReadyToSubmit));
        Assert.False(Transitions.Allowed(ApplicationState.SubmissionUnknown, ApplicationState.Submitting));
    }

    [Fact]
    public void So_pronta_para_enviar_entra_em_envio()
    {
        var into = Transitions.All.Where(kv => kv.Value.Contains(ApplicationState.Submitting)).Select(kv => kv.Key).ToList();
        Assert.Equal([ApplicationState.ReadyToSubmit], into);
    }

    [Fact]
    public void Estados_terminais_nao_tem_saida()
    {
        foreach (var s in new[] { ApplicationState.SubmittedConfirmed, ApplicationState.Failed, ApplicationState.Withdrawn, ApplicationState.DeadLetter })
            Assert.Empty(Transitions.All[s]);
    }

    [Fact]
    public void Todo_estado_tem_entrada_no_grafo()
    {
        foreach (var s in Enum.GetValues<ApplicationState>()) Assert.True(Transitions.All.ContainsKey(s), s.ToString());
    }

    [Fact]
    public void Confirmacao_sem_evidencia_forte_e_recusada()
    {
        // The copilot's path: ready for you, then the confirmation page in your browser (level 3) or a receipt (level 4).
        var app = Prepared();
        app.Move(ApplicationState.ManualHandoff, "teste", "teste", Now);
        Assert.Throws<DomainRuleException>(() => app.Confirmed(new SubmissionReceipt("", 2, "clicou"), "teste", Now));
        app.Confirmed(new SubmissionReceipt("", 3, "página de confirmação"), "Copiloto", Now);
        Assert.Equal(ApplicationState.SubmittedConfirmed, app.State);
    }

    [Fact]
    public void Transicao_fora_do_grafo_e_recusada_e_historico_registra_as_validas()
    {
        var app = JobApplication.Create(Job(), Now);
        Assert.Throws<DomainRuleException>(() => app.Move(ApplicationState.Submitting, "atalho", "teste", Now));
        app.MaterialsReady("/tmp/cv.pdf", "hash", Now);
        Assert.Equal(2, app.History.Count);
    }

    internal static JobShortlistedV1 Job() => new(Guid.NewGuid(), Guid.NewGuid(), "ats:x:1", "Empresa", "Backend Engineer", "Remote", "http://127.0.0.1:5470/jobs/1/apply", 90);

    private static JobApplication Prepared()
    {
        var app = JobApplication.Create(Job(), Now);
        app.MaterialsReady("/tmp/cv.pdf", "hash", Now);
        app.Move(ApplicationState.FormPreparing, "teste", "teste", Now);
        app.Prepared(new PreparedForm("a", "1", "x", new string('a', 64), []), [], Now);
        return app;
    }
}
