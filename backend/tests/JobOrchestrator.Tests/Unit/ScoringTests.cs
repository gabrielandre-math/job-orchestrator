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

    /// <summary>Qualquer área: enfermagem, com cargos-alvo e palavras da área, sem nenhuma tecnologia.</summary>
    private static readonly CampaignSettings Nursing = new(Guid.NewGuid(), "Enfermagem", true, SubmissionMode.PrepareOnly, 70, 15,
        ["enfermagem", "UTI", "COREN", "plantão"], ["São Paulo", "Remoto"], [], "America/Sao_Paulo", ["Enfermeira", "Enfermeiro de UTI", "Técnica de enfermagem"]);

    [Fact]
    public void Vaga_de_enfermagem_tem_nota_alta_com_cargo_alvo()
    {
        var e = Scoring.Evaluate(Nursing, "Enfermeira de UTI", "Hospital busca enfermeira com COREN ativo para plantão na UTI adulto. Experiência em enfermagem intensiva.", "São Paulo, SP", "Presencial");
        Assert.Null(e.Rejection);
        Assert.Equal(1, e.Factors.First(f => f.Name == "Cargo").Points / 25.0, 2);
        Assert.True(e.Score >= 70, $"nota {e.Score}");
    }

    [Fact]
    public void Cargo_de_outra_area_pesa_menos_que_cargo_alvo()
    {
        var alvo = Scoring.Evaluate(Nursing, "Enfermeira", "Enfermagem hospitalar com COREN.", "São Paulo");
        var outro = Scoring.Evaluate(Nursing, "Recepcionista", "Atendimento na recepção de clínica de enfermagem.", "São Paulo");
        Assert.True(alvo.Score > outro.Score, $"{alvo.Score} > {outro.Score}");
    }

    [Theory]
    [InlineData("Auxiliar administrativo", "Para começar é preciso pagar a taxa de inscrição de R$ 50 via Pix.")]
    [InlineData("Digitador home office", "Renda extra garantida! Contato só pelo WhatsApp.")]
    [InlineData("Data entry clerk", "A small application fee is required before the interview.")]
    public void Sentinela_corta_vaga_que_cobra_ou_so_fala_por_mensageiro(string title, string description) =>
        Assert.Contains("Suspeita de golpe", Scoring.Evaluate(Campaign with { Technologies = [] }, title, description, "Remoto").Rejection);

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

    private static readonly Preferences OnlyRemote = new(["remote"], 9000, 12000, 5000);

    [Theory]
    [InlineData("Presencial", null, null, null, null, "Presencial, e você aceita só remoto.")]
    [InlineData("Híbrido", null, null, null, null, "Híbrido, e você aceita só remoto.")]
    [InlineData("Remoto", null, null, null, null, null)]
    [InlineData(null, null, null, null, null, null)]
    [InlineData("Remoto", 6000, 8000, "BRL", "clt", "Paga até BRL 8.000 por mês, abaixo do seu piso CLT de 9.000.")]
    [InlineData("Remoto", 10000, 11000, "BRL", "pj", "Paga até BRL 11.000 por mês, abaixo do seu piso PJ de 12.000.")]
    [InlineData("Remoto", 9500, 10000, "BRL", "clt", null)]
    [InlineData("Remoto", 50000, 60000, "USD", null, null)]
    [InlineData("Remoto", 40000, 48000, "USD", null, "Paga até USD 4.000 por mês, abaixo do seu piso em dólar de 5.000.")]
    [InlineData("Remoto", 90000, 120000, "USD", null, null)]
    public void Your_limits_cut_only_what_the_job_states(string? modality, int? min, int? max, string? currency, string? contract, string? reason)
    {
        var pay = new JobPay(min, max, currency, contract);
        var broken = Scoring.Breaks(OnlyRemote, modality, pay);
        Assert.Equal(reason, broken);
    }

    [Fact]
    public void A_job_below_your_floor_leaves_the_queue_with_the_reason()
    {
        var e = Scoring.Evaluate(Campaign, "Senior Backend Engineer (.NET)", "C# and .NET with ASP.NET Core, PostgreSQL and AWS. Remote.", "Remote (LATAM)", "Remoto",
            OnlyRemote, new JobPay(3000, 4000, "USD", null));
        Assert.Equal(0, e.Score);
        Assert.Contains("abaixo do seu piso", e.Rejection);
    }
}
