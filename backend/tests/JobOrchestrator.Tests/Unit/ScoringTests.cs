using JobOrchestrator.Automation.Adapters;
using JobOrchestrator.Jobs.Domain;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Filtros eliminatórios vêm antes da nota; identidade canônica junta a mesma vaga vinda de fontes diferentes.</summary>
public sealed class ScoringTests
{
    private static readonly CampaignSettings Campaign = new(Guid.NewGuid(), "c", true, SubmissionMode.PrepareOnly, 80, 15,
        ["C#", ".NET", "ASP.NET Core", "AWS", "PostgreSQL"], ["Brasil", "Remoto"], [], "America/Sao_Paulo");

    [Fact]
    public void Vaga_dotnet_remota_tem_nota_alta_e_explicada()
    {
        var e = Scoring.Evaluate(Campaign, "Senior Backend Engineer (.NET)", "C# and .NET with ASP.NET Core, PostgreSQL and AWS. Remote.", "Remote (LATAM)");
        Assert.Null(e.Rejection);
        Assert.True(e.Score >= 90, $"nota {e.Score}");
        Assert.All(e.Factors, f => Assert.False(string.IsNullOrWhiteSpace(f.Note)));
    }

    [Fact] public void Restrita_a_outro_pais_e_cortada() => Assert.NotNull(Scoring.Evaluate(Campaign, "Senior .NET Engineer", "C# .NET. US only.", "Remote (US only)").Rejection);
    [Fact] public void Sem_stack_essencial_e_cortada() => Assert.Contains("Stack essencial", Scoring.Evaluate(Campaign, "Go Engineer", "Go and Kubernetes", "Remote").Rejection);
    [Fact] public void Local_brazil_casa_com_brasil() => Assert.Null(Scoring.Evaluate(Campaign, "Backend Developer .NET", "C# .NET", "São Paulo, Brazil").Rejection);

    [Fact]
    public void Mesma_vaga_de_duas_fontes_tem_a_mesma_identidade()
    {
        var a = Fingerprint.For("Norte Pagamentos", "Senior Backend Engineer (.NET)", "Remote", "http://127.0.0.1:5470/jobs/101/apply");
        var b = Fingerprint.For("Norte Pagamentos Ltda", "Sr. Backend Engineer", "LATAM", "http://127.0.0.1:5470/jobs/101/apply?utm_source=agregador");
        Assert.Equal(a, b);
        Assert.NotEqual(a, Fingerprint.For("Norte Pagamentos", "Senior Backend Engineer (.NET)", "Remote", "http://127.0.0.1:5470/jobs/102/apply"));
    }

    [Fact]
    public void Navegador_so_vai_aos_hosts_permitidos()
    {
        string[] allowed = ["127.0.0.1:5470", "boards.greenhouse.io"];
        Assert.True(BrowserPool.Allowed("http://127.0.0.1:5470/jobs/1/apply", allowed));
        Assert.True(BrowserPool.Allowed("https://boards.greenhouse.io/acme/jobs/1", allowed));
        Assert.False(BrowserPool.Allowed("http://127.0.0.1:5432/", allowed));
        Assert.False(BrowserPool.Allowed("http://192.168.0.10/admin", allowed));
        Assert.False(BrowserPool.Allowed("file:///etc/passwd", allowed));
        Assert.False(BrowserPool.Allowed("https://evil.example.com/", allowed));
    }

    [Fact]
    public void Token_de_envio_nao_aceita_payload_trocado_nem_vencido()
    {
        var key = new byte[32];
        Random.Shared.NextBytes(key);
        var now = DateTime.UtcNow;
        var intent = Guid.NewGuid(); var app = Guid.NewGuid();
        var token = new SubmitToken(intent, app, "abc", now.AddMinutes(10), SubmitToken.Sign(key, intent, app, "abc", now.AddMinutes(10)));
        Assert.True(token.IsValid(key, now));
        Assert.False((token with { PayloadHash = "abd" }).IsValid(key, now));
        Assert.False(token.IsValid(key, now.AddMinutes(11)));
        Assert.False(token.IsValid(new byte[32], now));
    }
}
