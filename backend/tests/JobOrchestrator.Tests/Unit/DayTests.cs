using JobOrchestrator.Applications.Domain;

namespace JobOrchestrator.Tests.Unit;

public sealed class DayTests
{
    [Fact] public void O_dia_vira_no_fuso_da_campanha() =>
        Assert.Equal(new DateTime(2026, 11, 5, 3, 0, 0), Day.Start(new DateTime(2026, 11, 6, 2, 0, 0, DateTimeKind.Utc), "America/Sao_Paulo"));
    [Fact] public void Fuso_desconhecido_vira_a_meia_noite_utc() =>
        Assert.Equal(new DateTime(2026, 11, 6), Day.Start(new DateTime(2026, 11, 6, 2, 0, 0, DateTimeKind.Utc), "Lugar/Nenhum"));
}
