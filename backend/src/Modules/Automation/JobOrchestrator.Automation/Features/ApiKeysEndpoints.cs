using JobOrchestrator.Automation.Adapters;
namespace JobOrchestrator.Automation.Features;

internal sealed record KeyInput(Dictionary<string, string>? Values);
internal sealed record KeyView(string Id, string Name, IReadOnlyList<string> Fields, string Help, string Adds, bool Connected);
internal sealed record KeyTest(bool Ok, string Detail);

/// <summary>
/// Chaves de API das fontes: lista (sem nunca devolver o valor), salva no gnome-keyring testando na hora e remove. Chave que não funciona
/// no teste sai do chaveiro e a tela diz o motivo.
/// </summary>
internal sealed class ApiKeysEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/sources/keys").WithTags("Automation");
        g.MapGet("", async (CancellationToken ct) =>
        {
            var list = new List<KeyView>();
            foreach (var k in ApiKeys.Catalog) list.Add(new KeyView(k.Id, k.Name, k.Fields, k.Help, k.Adds, await ApiKeys.HasAsync(k.Id, ct)));
            return Results.Ok(list);
        });
        g.MapPut("{id}", async (string id, KeyInput body, IEnumerable<ISourceConnector> sources, Leads leads, IHttpClientFactory http, CancellationToken ct) =>
        {
            var info = ApiKeys.Catalog.FirstOrDefault(k => k.Id == id);
            if (info is null) return Results.NotFound();
            var values = body.Values ?? [];
            if (info.Fields.Any(f => !values.TryGetValue(f, out var v) || string.IsNullOrWhiteSpace(v))) return Results.Ok(new KeyTest(false, "Preencha todos os campos."));
            if (!await ApiKeys.StoreAsync(id, values, ct)) return Results.Ok(new KeyTest(false, "O chaveiro do sistema (gnome-keyring) não aceitou a chave."));
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                var detail = id switch
                {
                    "adzuna" or "jooble" => $"Funcionou: {(await sources.First(s => s.Accepts($"{id}:vendas")).DiscoverAsync($"{id}:vendas", timeout.Token)).Count} vagas de teste para \"vendas\".",
                    "brave" => $"Funcionou: {(await leads.WebSearchAsync("vagas de emprego Brasil", timeout.Token)).Count} pistas de teste.",
                    _ => await GitHubAsync(http, values["token"], timeout.Token),
                };
                return Results.Ok(new KeyTest(true, detail));
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException or KeyNotFoundException or TaskCanceledException)
            {
                await ApiKeys.ClearAsync(id, ct);
                return Results.Ok(new KeyTest(false, ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden }
                    ? "O serviço recusou a chave. Confira se copiou inteira." : $"A chave não funcionou: {ex.Message}"));
            }
        });
        g.MapDelete("{id}", async (string id, CancellationToken ct) => { await ApiKeys.ClearAsync(id, ct); return Results.NoContent(); });
    }

    private static async Task<string> GitHubAsync(IHttpClientFactory http, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/rate_limit");
        request.Headers.Authorization = new("Bearer", token.Trim());
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.CreateClient("sources").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await HttpBodies.JsonAsync(response.Content, ct);
        return $"Funcionou: {doc.RootElement.GetProperty("resources").GetProperty("core").GetProperty("limit").GetInt32()} consultas por hora.";
    }
}
