using JobOrchestrator.BuildingBlocks;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Quadro de candidaturas: depois do envio, você marca entrevista, oferta, aceita ou recusada; antes do envio, quem move é a máquina.</summary>
public sealed class OutcomeTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 2, 0, 0, DateTimeKind.Utc);

    private static JobApplication Sent()
    {
        var app = JobApplication.Create(TransitionTests.Job(), Now);
        app.MaterialsReady("/tmp/cv.pdf", "hash", Now);
        app.Move(ApplicationState.ManualHandoff, "pelo site", "teste", Now);
        app.Move(ApplicationState.SubmittedDeclared, "enviada por você", "Você", Now);
        return app;
    }

    [Fact]
    public void Etapa_depois_do_envio_vai_para_o_historico_e_volta_para_enviada()
    {
        var app = Sent();
        var before = app.History.Count;
        app.SetOutcome("Entrevista", Now.AddDays(3));
        app.SetOutcome("Oferta", Now.AddDays(9));
        Assert.Equal(("Oferta", Now.AddDays(9)), (app.Outcome, app.OutcomeAt));
        Assert.Equal(ApplicationState.SubmittedDeclared, app.State);
        Assert.Equal(before + 2, app.History.Count);
        Assert.Equal(("Entrevista", "Oferta", "Você"), (app.History[^1].From, app.History[^1].To, app.History[^1].Actor));
        app.SetOutcome(null, Now.AddDays(10));
        Assert.Null(app.Outcome);
        Assert.Equal("Enviada", app.History[^1].To);
        // A mesma etapa de novo não repete o histórico.
        app.SetOutcome(null, Now.AddDays(11));
        Assert.Equal(before + 3, app.History.Count);
    }

    [Fact]
    public void Antes_do_envio_e_etapa_desconhecida_sao_recusadas()
    {
        var app = JobApplication.Create(TransitionTests.Job(), Now);
        Assert.Throws<DomainRuleException>(() => app.SetOutcome("Entrevista", Now));
        Assert.Throws<DomainRuleException>(() => Sent().SetOutcome("Contratada", Now));
    }
}
