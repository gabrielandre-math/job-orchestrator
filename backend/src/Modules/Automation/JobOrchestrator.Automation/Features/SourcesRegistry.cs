namespace JobOrchestrator.Automation.Features;

internal sealed record ProbeResult(string Prefix, string? Source, bool Ok, int Count, int? Http, double Seconds, string? Error, DateTime At);
internal sealed record ProbeInput(string[]? Samples);
internal sealed record RegistryLive(string Prefix, string Name, string Kind, string Access, string Lifecycle, string[] Countries, string DocsUrl, string? TermsUrl,
    string? Attribution, string Refresh, string[] CatalogIds, string Notes, string? Credential, bool CredentialPresent, bool Disabled, string? Gate, ProbeResult? Probe, bool Connector);
internal sealed record CatalogSummary(string Name, string CompiledOn, int Records, IReadOnlyDictionary<string, int> ByMethod, IReadOnlyDictionary<string, int> ByCategory, int MatchedToConnectors);
internal sealed record RegistryView(DateTime At, bool ExperimentalOn, IReadOnlyList<RegistryLive> Sources, CatalogSummary Catalog, DateTime? ProbedAt);
internal sealed record DisableInput(string Prefix, bool Disabled);
internal sealed record ExperimentalInput(bool On);

/// <summary>
/// O registro de fontes ao vivo (GET /api/sources/registry): cada perfil com o estado agora (chave presente, desligada, portão) e o resumo do
/// catálogo V2 do usuário, embutido no binário. PUT /api/sources/registry/disable liga e desliga uma plataforma inteira; Sources:Experimental
/// liga e desliga as observadas.
/// </summary>
internal sealed class SourcesRegistryEndpoints : ICarterModule
{
    private static readonly Lazy<JsonDocument> Catalog = new(() =>
    {
        using var stream = typeof(SourcesRegistryEndpoints).Assembly.GetManifestResourceStream("catalogo-fontes-v2.json")!;
        return JsonDocument.Parse(stream);
    });

    public static string? Gate(string source, IConfiguration config, TimeProvider time) =>
        SourceRegistry.Gate(source, config.GetSection("Sources:Disabled").Get<string[]>() ?? [], config.GetValue("Sources:Experimental", true), time.GetUtcNow().UtcDateTime);

    private static string ProbeFile => Path.Combine(Directory.CreateDirectory(Path.Combine(AppPaths.Data, "sources")).FullName, "probe.json");

    /// <summary>A última sondagem de cada plataforma (data/sources/probe.json na pasta de dados).</summary>
    public static IReadOnlyDictionary<string, ProbeResult> LastProbe()
    {
        try
        {
            return File.Exists(ProbeFile)
                ? (JsonSerializer.Deserialize<List<ProbeResult>>(File.ReadAllText(ProbeFile), JsonSerializerOptions.Web) ?? []).ToDictionary(r => r.Prefix)
                : new Dictionary<string, ProbeResult>();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return new Dictionary<string, ProbeResult>(); }
    }

    /// <summary>Os registros do catálogo V2 (id, nome, categoria, método, escopo, país, url, status de verificação).</summary>
    public static IEnumerable<JsonElement> CatalogRecords() => Catalog.Value.RootElement.GetProperty("sources").EnumerateArray();

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sources/registry", async (IConfiguration config, TimeProvider time, IEnumerable<ISourceConnector> connectors, CancellationToken ct) =>
        {
            var disabled = config.GetSection("Sources:Disabled").Get<string[]>() ?? [];
            var probe = LastProbe();
            var known = connectors.ToList();
            var list = new List<RegistryLive>();
            foreach (var p in SourceRegistry.All)
            {
                var present = p.Credential is { } key && await ApiKeys.HasAsync(key, ct);
                list.Add(new RegistryLive(p.Prefix, p.Name, p.Kind, p.Access, present && p.Lifecycle == "CREDENTIALS_REQUIRED" ? "ACTIVE" : p.Lifecycle, p.Countries, p.DocsUrl, p.TermsUrl,
                    p.Attribution, p.Refresh, p.CatalogIds, p.Notes, p.Credential, present, disabled.Contains(p.Prefix, StringComparer.OrdinalIgnoreCase), Gate(p.Prefix + ":x", config, time),
                    probe.GetValueOrDefault(p.Prefix), known.Any(c => c.Accepts(p.Prefix + ":x"))));
            }
            var records = CatalogRecords().ToList();
            var matched = SourceRegistry.All.SelectMany(p => p.CatalogIds).ToHashSet();
            var root = Catalog.Value.RootElement;
            var summary = new CatalogSummary(root.GetProperty("name").GetString() ?? "", root.GetProperty("compiled_on").GetString() ?? "", records.Count,
                records.GroupBy(r => r.GetProperty("method").GetString() ?? "").ToDictionary(g => g.Key, g => g.Count()),
                records.GroupBy(r => r.GetProperty("category").GetString() ?? "").ToDictionary(g => g.Key, g => g.Count()),
                records.Count(r => matched.Contains(r.GetProperty("id").GetString() ?? "")));
            return Results.Ok(new RegistryView(time.GetUtcNow().UtcDateTime, config.GetValue("Sources:Experimental", true), list, summary,
                probe.Count == 0 ? null : probe.Values.Max(r => r.At)));
        }).WithTags("Automation");

        // Sondagem real: cada plataforma com permissão é chamada uma vez pela porta oficial (amostra da campanha ou pública), quatro por vez.
        // Bloqueada e só com parceria nunca são chamadas. O resultado fica na pasta de dados e aparece no registro e na auditoria.
        app.MapPost("/api/sources/registry/probe", async (ProbeInput body, IEnumerable<ISourceConnector> sources, IConfiguration config, TimeProvider time, CancellationToken ct) =>
        {
            var samples = (body.Samples ?? []).Select(s => s.Trim()).Where(s => s.Contains(':')).ToList();
            var connectors = sources.ToList();
            using var slots = new SemaphoreSlim(4);
            var results = await Task.WhenAll(SourceRegistry.All.Select(async p =>
            {
                var at = time.GetUtcNow().UtcDateTime;
                if (p.Lifecycle is "BLOCKED" or "PARTNER_ONLY") return new ProbeResult(p.Prefix, null, false, 0, null, 0, $"não sondada: {p.Lifecycle}", at);
                var sample = samples.FirstOrDefault(s => s.StartsWith(p.Prefix + ":", StringComparison.OrdinalIgnoreCase)) ?? SourceRegistry.Samples.GetValueOrDefault(p.Prefix);
                if (sample is null) return new ProbeResult(p.Prefix, null, false, 0, null, 0, "sem amostra pública segura", at);
                await slots.WaitAsync(ct);
                try
                {
                    var r = await SourceProbe.RunAsync(sample, connectors, config, time, TimeSpan.FromSeconds(45), ct);
                    return new ProbeResult(p.Prefix, sample, r.Ok, r.Count, r.Http, r.Seconds, r.Error, time.GetUtcNow().UtcDateTime);
                }
                finally { slots.Release(); }
            }));
            await File.WriteAllTextAsync(ProbeFile, JsonSerializer.Serialize(results, JsonSerializerOptions.Web), ct);
            return Results.Ok(results);
        }).WithTags("Automation");

        app.MapPut("/api/sources/registry/experimental", async (ExperimentalInput body) =>
        {
            await UserSettings.SetAsync(new Dictionary<string, object?> { ["Sources:Experimental"] = body.On });
            return Results.NoContent();
        }).WithTags("Automation");

        app.MapPut("/api/sources/registry/disable", async (DisableInput body, IConfiguration config) =>
        {
            if (SourceRegistry.All.All(p => p.Prefix != body.Prefix)) return Results.BadRequest();
            var now = (config.GetSection("Sources:Disabled").Get<string[]>() ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (body.Disabled) now.Add(body.Prefix); else now.Remove(body.Prefix);
            var values = new Dictionary<string, object?>();
            // A lista vai inteira para o settings.json (índices 0..n); os índices que sobrarem da lista antiga são apagados.
            var list = now.Order().ToList();
            for (var i = 0; i < Math.Max(list.Count, 60); i++) values[$"Sources:Disabled:{i}"] = i < list.Count ? list[i] : null;
            await UserSettings.SetAsync(values);
            return Results.NoContent();
        }).WithTags("Automation");
    }
}
