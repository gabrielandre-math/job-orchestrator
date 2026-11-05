using System.Diagnostics;

namespace JobOrchestrator.Automation.Features;

internal sealed record SourceTest(string Source);
internal sealed record SourceSample(string Title, string Company, string Location, string ApplyUrl, string? ApplyEmail);
internal sealed record SourceTestResult(bool Ok, int Count, double Seconds, IReadOnlyList<SourceSample> Sample, string? Error);

/// <summary>
/// Teste de uma fonte antes de entrar na campanha (assistente "Adicionar fonte"): consulta pela porta oficial, com teto de 60 s,
/// e devolve quantas vagas vieram e as primeiras. Erro vira frase, nunca exceção na tela.
/// </summary>
internal sealed class SourcesTestEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/api/sources/test", async (SourceTest body, IEnumerable<ISourceConnector> sources, CancellationToken ct) =>
        {
            var source = body.Source?.Trim() ?? "";
            var connector = sources.FirstOrDefault(s => s.Accepts(source));
            if (source.Length < 4 || connector is null) return Results.Ok(new SourceTestResult(false, 0, 0, [], "Não reconheci esta fonte. Confira a plataforma e o nome da empresa."));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var clock = Stopwatch.StartNew();
            try
            {
                var found = await connector.DiscoverAsync(source, timeout.Token);
                var sample = found.Take(6).Select(p => new SourceSample(p.Title, p.Company, p.Location, p.ApplyUrl, p.ApplyEmail)).ToList();
                return Results.Ok(new SourceTestResult(true, found.Count, Math.Round(clock.Elapsed.TotalSeconds, 1), sample,
                    found.Count == 0 ? "A fonte respondeu, mas não tem vaga aberta agora. Pode salvar: ela entra nas próximas rodadas." : null));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
            {
                var why = ex switch
                {
                    HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } => "A empresa não foi encontrada nesta plataforma. Confira o nome como aparece no endereço das vagas.",
                    HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } => "O site recusou a consulta (403). Sem contorno: escolha outra fonte para esta empresa.",
                    TaskCanceledException => "A fonte demorou mais de um minuto para responder.",
                    _ => $"A consulta falhou: {(ex.Message.Length > 160 ? ex.Message[..160] : ex.Message)}",
                };
                return Results.Ok(new SourceTestResult(false, 0, Math.Round(clock.Elapsed.TotalSeconds, 1), [], why));
            }
        }).WithTags("Automation");
}
