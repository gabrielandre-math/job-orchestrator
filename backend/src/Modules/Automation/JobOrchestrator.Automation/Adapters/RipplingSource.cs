namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Rippling (rippling:empresa): o endereço que o próprio quadro público ats.rippling.com usa (observado, sem contrato), com a lista e o detalhe
/// de cada vaga (até 40 por rodada, uma de cada vez). Vaga marcada unlistedFromSearch não aparece. O api.rippling.com não tem robots.txt
/// que feche a rota; o de ats.rippling.com só fecha /internal/.
/// </summary>
internal sealed class RipplingSource(IHttpClientFactory http) : PublicSource(http, "rippling", "ats.rippling.com", "api.rippling.com")
{
    private const int MaxDetails = 40;
    public override string Id => "rippling-board";
    public override string Name => "Rippling ATS";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var board = Key(source).Trim('/');
        var root = $"https://api.rippling.com/platform/api/ats/v1/board/{Uri.EscapeDataString(board)}/jobs";
        using var list = await Json.GetAsync(Client, root, ct);
        var result = new List<DiscoveredPosting>();
        foreach (var j in list.RootElement.EnumerateArray().Take(MaxDetails))
        {
            var uuid = j.Str("uuid");
            using var doc = await Json.GetAsync(Client, $"{root}/{Uri.EscapeDataString(uuid)}", ct);
            if (Parse(source, board, doc.RootElement) is { } p) result.Add(p);
            await Task.Delay(150, ct);
        }
        return result;
    }

    internal static DiscoveredPosting? Parse(string source, string board, JsonElement d)
    {
        if (d.TryGetProperty("unlistedFromSearch", out var u) && u.ValueKind == JsonValueKind.True) return null;
        var text = d.TryGetProperty("description", out var desc) ? desc.ValueKind switch
        {
            JsonValueKind.Object => string.Join("\n", desc.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.String).Select(x => x.Value.GetString())),
            JsonValueKind.String => desc.GetString() ?? "",
            _ => "",
        } : "";
        var where = d.TryGetProperty("workLocations", out var w) && w.ValueKind == JsonValueKind.Array
            ? string.Join("; ", w.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.Str("label")).Where(x => !string.IsNullOrWhiteSpace(x))) : "";
        string? pay = null;
        if (d.TryGetProperty("payRangeDetails", out var pr) && pr.ValueKind == JsonValueKind.Array && pr.GetArrayLength() > 0)
        {
            var r = pr[0];
            pay = $"{r.Str("currency")} {Num(r, "rangeStart")}-{Num(r, "rangeEnd")}/{r.Str("frequency").ToLowerInvariant()}".Trim();
        }
        var kind = d.TryGetProperty("employmentType", out var e) ? e.ValueKind == JsonValueKind.Object ? e.Str("id") : e.ValueKind == JsonValueKind.String ? e.GetString() : null : null;
        return new DiscoveredPosting(source, d.Str("uuid"), d.Str("companyName") is { Length: > 0 } c ? c : Json.Company(board), d.Str("name"), where, d.Str("url"),
            Json.Plain(text), d.Date("createdOn"), d.GetRawText(), Compensation: pay, Employment: kind, ListedAsOpen: true);
    }

    private static string Num(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "";
}
