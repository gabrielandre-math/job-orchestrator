using System.Text.Json;
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

    /// <summary>Prepared in full (the form filled and checked) and waiting for the person to send it, with the copilot.</summary>
    private static async Task ReadyForYouAsync(Stack s, string company)
    {
        Assert.Equal("ManualHandoff", await s.StateAsync(company));
        var app = await s.ApplicationAsync(company);
        var detail = await s.GetAsync($"/applications/{app!.Value.GetProperty("id").GetString()}");
        Assert.False(string.IsNullOrEmpty(detail.GetProperty("payloadHash").GetString()), $"{company} sem formulário preparado");
    }

    [Fact]
    public async Task So_preparar_nao_envia_nada_e_pausa_no_que_precisa_de_voce()
    {
        await using var s = await Stack.StartAsync();
        await s.RunAsync();
        await SettledAsync(s, 4);
        await ReadyForYouAsync(s, "Norte Pagamentos");
        await ReadyForYouAsync(s, "Ponte Logística");
        // A CAPTCHA is the site's own wall: the application goes by the site, with you; a question is what "needs you".
        Assert.Equal("ManualHandoff", await s.StateAsync("Atlas Saúde"));
        Assert.Equal("NeedsHuman", await s.StateAsync("Lume Educação"));
        Assert.Equal(0, await s.SubmissionsAsync());
        // The open question reaches the workflow whole (kind and all), for the AI to answer; here the AI is off, so it stops with you.
        var lume = await s.ApplicationAsync("Lume Educação");
        var stop = (await s.GetAsync($"/applications/{lume!.Value.GetProperty("id").GetString()}")).GetProperty("history").EnumerateArray()
            .Last(h => h.GetProperty("to").GetString() == "NeedsHuman").GetProperty("detail").GetString()!;
        Assert.Contains("\"kind\":\"textarea\"", stop.Replace(" ", ""));
        Assert.Contains("favorite design pattern", stop);

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
        await ReadyForYouAsync(s, "Aurora Ashby");   // 2: Ashby com campos extras
        await ReadyForYouAsync(s, "Farol Lever");    // 3: Lever com upload e pergunta condicional
        await ReadyForYouAsync(s, "Muda Sempre");    // 4: seletor que muda a cada carga
        Assert.Equal("ManualHandoff", await s.StateAsync("Cofre Seguro"));   // 5: MFA, the site's wall: by the site, with you
        // 9: prompt injection na descrição é só dado: a vaga segue o caminho normal, a campanha não muda de modo e nada sai.
        await ReadyForYouAsync(s, "Eco Injeção");
        Assert.Equal("PrepareOnly", (await s.GetAsync("/campaigns"))[0].GetProperty("mode").GetString());
        Assert.Equal(0, await s.SubmissionsAsync());
    }

    [Fact]
    public async Task Site_sem_adapter_proprio_fica_pronto_para_voce_com_a_declaracao_so_no_preparo()
    {
        await using var s = await Stack.StartAsync();
        await s.SourcesAsync("PrepareOnly", $"greenhouse:{s.Ats}/boards/site");
        await s.RunAsync();
        // A link to a site with no adapter of its own: the form opens behind "Candidatar-se", has no form tag and labels beside the fields.
        await s.WaitAsync(async () => await s.StateAsync("Brisa Sistemas") == "ManualHandoff", Settle, "o formulário aberto do site ficar pronto");
        await ReadyForYouAsync(s, "Brisa Sistemas");
        Assert.Equal(0, await s.SubmissionsAsync(501));
        var app = await s.ApplicationAsync("Brisa Sistemas");
        var fields = (await s.GetAsync($"/applications/{app!.Value.GetProperty("id").GetString()}")).GetProperty("fields").EnumerateArray()
            .Select(f => $"{f.GetProperty("label").GetString()}={f.GetProperty("value").GetString()}").ToList();
        // The privacy box is ticked only in the prepare's own browser, which never sends (on the site, you tick it); the newsletter stays blank.
        Assert.Contains(fields, f => f.StartsWith("Li e aceito a Política de Privacidade", StringComparison.Ordinal) && f.EndsWith("=Aceito", StringComparison.Ordinal));
        Assert.DoesNotContain(fields, f => f.Contains("novidades", StringComparison.Ordinal));
    }
}
