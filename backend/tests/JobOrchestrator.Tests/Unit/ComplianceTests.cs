using System.Text.Json;
using JobOrchestrator.Automation.Adapters;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Conformidade por fonte (V2): Ashby não mostra vaga não publicada e a Remotive não passa de 4 leituras por dia.</summary>
public sealed class ComplianceTests
{
    [Fact]
    public void Ashby_esconde_vaga_com_isListed_false()
    {
        using var doc = JsonDocument.Parse("""[{"id":"1","isListed":true},{"id":"2","isListed":false},{"id":"3"}]""");
        var listed = doc.RootElement.EnumerateArray().Where(AshbySource.Listed).Select(j => j.GetProperty("id").GetString()).ToList();
        Assert.Equal(["1", "3"], listed);
    }

    [Fact]
    public void Remotive_para_no_teto_do_dia_e_volta_no_dia_seguinte()
    {
        var day = new DateOnly(2099, 1, 1);
        for (var i = 0; i < RemotiveSource.DailyCap; i++) Assert.True(RemotiveSource.Take(day));
        Assert.False(RemotiveSource.Take(day));
        Assert.True(RemotiveSource.Take(day.AddDays(1)));
    }
}
