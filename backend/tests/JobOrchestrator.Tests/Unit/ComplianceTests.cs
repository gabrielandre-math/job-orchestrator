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
    public void Rippling_esconde_vaga_fora_da_busca_e_monta_a_vaga_do_detalhe()
    {
        using var hidden = JsonDocument.Parse("""{"uuid":"a","name":"X","unlistedFromSearch":true}""");
        Assert.Null(RipplingSource.Parse("rippling:aaca", "aaca", hidden.RootElement));
        using var doc = JsonDocument.Parse("""
            {"uuid":"b","name":"Paraprofessional","companyName":"AACA","url":"https://ats.rippling.com/aaca/jobs/b","workLocations":["Lancaster, CA"],
             "description":{"company":"<p>Escola</p>","role":"<p>Apoio em sala</p>"},"employmentType":{"label":"HOURLY_FT","id":"Hourly, full-time"},
             "createdOn":"2026-10-02T20:09:23-07:00","payRangeDetails":[{"currency":"USD","frequency":"HOUR","rangeStart":22.0,"rangeEnd":25.0}],"unlistedFromSearch":false}
            """);
        var p = RipplingSource.Parse("rippling:aaca", "aaca", doc.RootElement)!;
        Assert.Equal("AACA", p.Company);
        Assert.Equal("Lancaster, CA", p.Location);
        Assert.Contains("Apoio em sala", p.Description);
        Assert.Equal("USD 22-25/hour", p.Compensation);
        Assert.Equal("Hourly, full-time", p.Employment);
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
