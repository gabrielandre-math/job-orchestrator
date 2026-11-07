using System.Net;

namespace JobOrchestrator.Tests.EndToEnd;

/// <summary>
/// Ponta a ponta com processos reais e o navegador. Precisa do Postgres de teste:
/// docker compose -f infra/compose.test.yaml up -d --wait
/// </summary>
[Trait("Category", "E2E")]
public sealed class FlowTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(90);
    private static readonly string[] Busy = ["Shortlisted", "MaterialsReady", "FormPreparing", "ReadyToSubmit", "Submitting", "SubmissionUnknown"];

    private static async Task SettledAsync(Stack s, int expected) => await s.WaitAsync(async () =>
    {
        var apps = (await s.GetAsync("/applications")).EnumerateArray().ToList();
        return apps.Count >= expected && apps.All(a => !Busy.Contains(a.GetProperty("state").GetString()));
    }, Settle, "candidaturas estabilizarem");

    [Fact]
    public async Task So_preparar_nao_envia_nada_e_pausa_no_que_precisa_de_voce()
    {
        await using var s = await Stack.StartAsync();
        await s.RunAsync();
        await SettledAsync(s, 4);
        Assert.Equal("FormValidated", await s.StateAsync("Norte Pagamentos"));
        Assert.Equal("FormValidated", await s.StateAsync("Ponte Logística"));
        Assert.Equal("NeedsHuman", await s.StateAsync("Atlas Saúde"));
        Assert.Equal("NeedsHuman", await s.StateAsync("Lume Educação"));
        Assert.Equal(0, await s.SubmissionsAsync());

        var jobs = (await s.GetAsync("/jobs")).EnumerateArray().GroupBy(j => j.GetProperty("status").GetString()).ToDictionary(g => g.Key!, g => g.Count());
        Assert.Equal(1, jobs["Duplicate"]);
        Assert.Equal(1, jobs["Expired"]);
        Assert.Equal(1, jobs["Rejected"]);
        // A vaga de Go nem entra: não cita nenhuma tecnologia essencial da campanha.
        Assert.DoesNotContain((await s.GetAsync("/jobs")).EnumerateArray(), j => j.GetProperty("title").GetString() == "Go Engineer");
    }

    [Fact]
    public async Task Cenarios_de_formulario_preparam_ou_param_no_que_precisa_de_voce()
    {
        await using var s = await Stack.StartAsync();
        await s.SourcesAsync("PrepareOnly", $"greenhouse:{s.Ats}/boards/cenarios");
        await s.RunAsync();
        await SettledAsync(s, 5);
        Assert.Equal("FormValidated", await s.StateAsync("Aurora Ashby"));   // 2: Ashby com campos extras
        Assert.Equal("FormValidated", await s.StateAsync("Farol Lever"));    // 3: Lever com upload e pergunta condicional
        Assert.Equal("FormValidated", await s.StateAsync("Muda Sempre"));    // 4: seletor que muda a cada carga
        Assert.Equal("NeedsHuman", await s.StateAsync("Cofre Seguro"));      // 5: MFA
        // 9: prompt injection na descrição é só dado: a vaga segue o caminho normal, a campanha não muda de modo e nada sai.
        Assert.Equal("FormValidated", await s.StateAsync("Eco Injeção"));
        Assert.Equal("PrepareOnly", (await s.GetAsync("/campaigns"))[0].GetProperty("mode").GetString());
        Assert.Equal(0, await s.SubmissionsAsync());
    }

    [Fact]
    public async Task Automatico_so_com_autorizacao_dentro_do_teto_e_revogar_volta_para_revisao()
    {
        await using var s = await Stack.StartAsync();
        var id = (await s.GetAsync("/campaigns"))[0].GetProperty("id").GetString();
        await s.ConfigureAsync("ReviewRequired");
        // O arquivo de configuração e o PUT da campanha não ligam o automático.
        Assert.Equal(HttpStatusCode.Forbidden, await s.PutAsync($"/campaigns/{id}", new { mode = "AutoSubmit", minFitScore = 80, maxSubmissionsPerDay = 15,
            technologies = new[] { "C#", ".NET" }, locations = new[] { "Brasil", "Remoto" }, sources = new[] { $"greenhouse:{s.Ats}/boards/demo" } }));
        Assert.Equal(HttpStatusCode.BadRequest, await s.PostAsync($"/campaigns/{id}/authorizations", new { days = 1, maxPerDay = 1, minScore = 80, adapters = new[] { "greenhouse-form" }, accepted = false }));
        Assert.Equal(HttpStatusCode.OK, await s.PostAsync($"/campaigns/{id}/authorizations", new { days = 1, maxPerDay = 1, minScore = 80, adapters = new[] { "greenhouse-form" }, accepted = true }));
        Assert.Equal("AutoSubmit", (await s.GetAsync("/campaigns"))[0].GetProperty("mode").GetString());

        await s.RunAsync();
        await s.WaitAsync(async () => await s.SubmissionsAsync() >= 1, Settle, "o primeiro envio automático");
        // Teto de 1 por dia da autorização: um envio só, mesmo com duas vagas prontas (a lenta conta enquanto está incerta).
        // A outra não se perde: continua pronta, com o envio para quando o dia virar.
        await s.WaitAsync(async () => (await s.GetAsync("/applications")).EnumerateArray().Any(a => a.GetProperty("state").GetString() == "ReadyToSubmit"
            && a.GetProperty("lastReason").GetString()?.Contains("fica para amanhã") == true), Settle, "a segunda pronta esperar o dia seguinte");
        Assert.Equal(1, await s.SubmissionsAsync());

        var auth = (await s.GetAsync($"/campaigns/{id}/authorizations"))[0].GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await s.Http.DeleteAsync($"{s.Api}/campaigns/{id}/authorizations/{auth}")).StatusCode);
        Assert.Equal("ReviewRequired", (await s.GetAsync("/campaigns"))[0].GetProperty("mode").GetString());
        // A que esperava o dia seguinte pela autorização volta já para a sua aprovação: nada sai sozinho depois de revogar.
        await s.WaitAsync(async () => (await s.GetAsync("/applications")).EnumerateArray().Any(a => a.GetProperty("state").GetString() == "AwaitingApproval"
            && a.GetProperty("lastReason").GetString()?.Contains("foi desligado") == true), Settle, "a pronta pela autorização voltar para você");
        Assert.Equal(1, await s.SubmissionsAsync());
    }

    [Fact]
    public async Task Aprovacao_envia_uma_vez_e_confirmacao_lenta_vira_reconciliacao()
    {
        await using var s = await Stack.StartAsync();
        await s.ConfigureAsync("ReviewRequired");
        await s.RunAsync();
        await SettledAsync(s, 4);
        await s.ApproveAsync("Norte Pagamentos");
        await s.ApproveAsync("Ponte Logística");
        await s.WaitAsync(async () => await s.StateAsync("Norte Pagamentos") == "SubmittedConfirmed" && await s.StateAsync("Ponte Logística") == "SubmittedConfirmed", Settle, "confirmações");
        Assert.Equal(1, await s.SubmissionsAsync(101));
        Assert.Equal(1, await s.SubmissionsAsync(103));
        Assert.Contains("SubmissionUnknown", await s.HistoryAsync("Ponte Logística"));
    }

    [Theory]
    [InlineData("after-click", 1, "SubmittedConfirmed")]
    [InlineData("before-click", 0, "NeedsHuman")]
    public async Task Queda_do_processo_no_envio_nunca_duplica(string crashAt, int expectedSubmissions, string finalState)
    {
        await using var s = await Stack.StartAsync(crashAt);
        await s.ConfigureAsync("ReviewRequired");
        await s.RunAsync();
        await SettledAsync(s, 4);
        await s.ApproveAsync("Norte Pagamentos");
        await s.WaitWorkerDeathAsync(TimeSpan.FromSeconds(60));
        s.StartWorker();
        await s.WaitAsync(async () => await s.StateAsync("Norte Pagamentos") == finalState, Settle, $"estado final {finalState}");
        Assert.Equal(expectedSubmissions, await s.SubmissionsAsync(101));
        Assert.Contains("SubmissionUnknown", await s.HistoryAsync("Norte Pagamentos"));
    }

    [Fact]
    public async Task Queda_durante_a_reconciliacao_retoma_sem_reenviar()
    {
        await using var s = await Stack.StartAsync("during-reconcile");
        await s.ConfigureAsync("ReviewRequired");
        await s.RunAsync();
        await SettledAsync(s, 4);
        await s.ApproveAsync("Ponte Logística");
        await s.WaitWorkerDeathAsync(TimeSpan.FromSeconds(90));
        s.StartWorker();
        await s.WaitAsync(async () => await s.StateAsync("Ponte Logística") == "SubmittedConfirmed", Settle, "confirmação depois da queda");
        Assert.Equal(1, await s.SubmissionsAsync(103));
    }

    [Fact]
    public async Task Site_sem_adapter_proprio_envia_pelo_formulario_aberto_com_a_declaracao_autorizada()
    {
        await using var s = await Stack.StartAsync();
        await s.SourcesAsync("ReviewRequired", $"greenhouse:{s.Ats}/boards/site");
        var id = (await s.GetAsync("/campaigns"))[0].GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, await s.PostAsync($"/campaigns/{id}/authorizations",
            new { days = 1, maxPerDay = 5, minScore = 80, adapters = Array.Empty<string>(), accepted = true, acceptDeclarations = true }));
        await s.RunAsync();
        // A link to a site with no adapter of its own: the form opens behind "Candidatar-se", has no form tag and labels beside the fields.
        await s.WaitAsync(async () => await s.StateAsync("Brisa Sistemas") == "SubmittedConfirmed", Settle, "o envio pelo formulário aberto do site");
        Assert.Equal(1, await s.SubmissionsAsync(501));
        var app = await s.ApplicationAsync("Brisa Sistemas");
        var fields = (await s.GetAsync($"/applications/{app!.Value.GetProperty("id").GetString()}")).GetProperty("fields").EnumerateArray()
            .Select(f => $"{f.GetProperty("label").GetString()}={f.GetProperty("value").GetString()}").ToList();
        // Privacy accepted by the authorization; the newsletter box stays blank.
        Assert.Contains(fields, f => f.StartsWith("Li e aceito a Política de Privacidade", StringComparison.Ordinal) && f.EndsWith("=Aceito", StringComparison.Ordinal));
        Assert.DoesNotContain(fields, f => f.Contains("novidades", StringComparison.Ordinal));
    }
}
