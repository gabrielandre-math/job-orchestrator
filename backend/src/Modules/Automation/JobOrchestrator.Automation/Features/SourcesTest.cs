using System.Diagnostics;

namespace JobOrchestrator.Automation.Features;

internal sealed record SourceTest(string Source);
internal sealed record SourceSample(string Title, string Company, string Location, string ApplyUrl, string? ApplyEmail);
internal sealed record SourceTestResult(bool Ok, int Count, double Seconds, IReadOnlyList<SourceSample> Sample, string? Error, int? Http = null);

/// <summary>
/// Teste de uma fonte antes de entrar na campanha (assistente "Adicionar fonte") e sondagem do registro: consulta pela porta oficial, com teto de
/// tempo, e devolve quantas vagas vieram, as primeiras e o código HTTP. Erro vira frase, nunca exceção na tela. Fonte sem permissão nem é chamada.
/// </summary>
internal static class SourceProbe
{
    public static async Task<SourceTestResult> RunAsync(string source, IEnumerable<ISourceConnector> sources, IConfiguration config, TimeProvider time, TimeSpan limit, CancellationToken ct)
    {
        source = source.Trim();
        if (SourcesRegistryEndpoints.Gate(source, config, time) is { } gate) return new SourceTestResult(false, 0, 0, [], gate);
        var connector = sources.FirstOrDefault(s => s.Accepts(source));
        if (source.Length < 4 || connector is null) return new SourceTestResult(false, 0, 0, [], "Não reconheci esta fonte. Confira a plataforma e o nome da empresa.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        var clock = Stopwatch.StartNew();
        try
        {
            var all = await connector.DiscoverAsync(source, timeout.Token);
            // Marcação de encerramento (tombstone) não é vaga: conta à parte.
            var found = all.Where(p => !p.Tombstone).ToList();
            var closed = all.Count - found.Count;
            var sample = found.Take(6).Select(p => new SourceSample(p.Title, p.Company, p.Location, p.ApplyUrl, p.ApplyEmail)).ToList();
            return new SourceTestResult(true, found.Count, Math.Round(clock.Elapsed.TotalSeconds, 1), sample,
                found.Count == 0 ? "A fonte respondeu, mas não tem vaga aberta agora. Pode salvar: ela entra nas próximas rodadas."
                : closed > 0 ? $"Também declarou {closed} {(closed == 1 ? "anúncio encerrado" : "anúncios encerrados")}, que fecham as vagas que já estiverem aqui." : null, 200);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException or KeyNotFoundException)
        {
            var why = ex switch
            {
                HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } => "A empresa não foi encontrada nesta plataforma. Confira o nome como aparece no endereço das vagas.",
                HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } => "O site recusou a consulta (403). Sem contorno: escolha outra fonte para esta empresa.",
                HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } => "A fonte pede credencial (401).",
                HttpRequestException { StatusCode: (System.Net.HttpStatusCode)429 } => "A fonte pediu para esperar (429): respeitamos o limite dela.",
                TaskCanceledException => $"A fonte demorou mais de {limit.TotalSeconds:0} s para responder.",
                _ => $"A consulta falhou: {(ex.Message.Length > 160 ? ex.Message[..160] : ex.Message)}",
            };
            return new SourceTestResult(false, 0, Math.Round(clock.Elapsed.TotalSeconds, 1), [], why, ex is HttpRequestException { StatusCode: { } code } ? (int)code : null);
        }
    }
}

internal sealed class SourcesTestEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/api/sources/test", async (SourceTest body, IEnumerable<ISourceConnector> sources, IConfiguration config, TimeProvider time, CancellationToken ct) =>
            Results.Ok(await SourceProbe.RunAsync(body.Source ?? "", sources, config, time, TimeSpan.FromSeconds(60), ct))).WithTags("Automation");
}
