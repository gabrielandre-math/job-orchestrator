using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// República Tcheca (mpsv:termo): o conjunto aberto e oficial de vagas do Úřad práce (MPSV, data.mpsv.cz), atualizado todo dia. Os termos de
/// uso do conjunto dizem que ele não tem obra protegida nem direito de base de dados, e que tem dados pessoais: os contatos (nome, telefone e
/// e-mail de quem atende) ficam de fora, inclusive do texto. O arquivo tem uns 186 MB: baixa o .gz uma vez por dia e lê item a item, sem
/// carregar tudo. As bandeiras oficiais da vaga (aceita estrangeiro de fora da UE, Cartão Azul, cartão de funcionário) entram no texto.
/// </summary>
internal sealed partial class MpsvSource(IHttpClientFactory http, TimeProvider time) : PublicSource(http, "mpsv", "data.mpsv.cz", "www.uradprace.cz")
{
    private const string Url = "https://data.mpsv.cz/od/soubory/volna-mista/volna-mista.json.gz";
    private const int Max = 300;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<(DateOnly Day, string Term), List<DiscoveredPosting>> Found = [];
    private static IReadOnlyDictionary<string, string>? towns;
    /// <summary>As 14 regiões do código oficial (data.mpsv.cz/od/soubory/ciselniky/kraje.json).</summary>
    private static readonly Dictionary<string, string> Regions = new()
    {
        ["Kraj/19"] = "Hlavní město Praha", ["Kraj/27"] = "Středočeský kraj", ["Kraj/35"] = "Jihočeský kraj", ["Kraj/43"] = "Plzeňský kraj", ["Kraj/51"] = "Karlovarský kraj",
        ["Kraj/60"] = "Ústecký kraj", ["Kraj/78"] = "Liberecký kraj", ["Kraj/86"] = "Královéhradecký kraj", ["Kraj/94"] = "Pardubický kraj", ["Kraj/108"] = "Kraj Vysočina",
        ["Kraj/116"] = "Jihomoravský kraj", ["Kraj/124"] = "Olomoucký kraj", ["Kraj/132"] = "Moravskoslezský kraj", ["Kraj/141"] = "Zlínský kraj",
    };
    public override string Id => "mpsv-volna-mista";
    public override string Name => "MPSV volná pracovní místa";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var term = NavSource.Fold(Key(source));
        var any = term is "" or "*" or "all";
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        await Gate.WaitAsync(ct);
        try
        {
            if (Found.TryGetValue((today, term), out var known)) return known;
            towns ??= await TownsAsync(ct);
            var list = new List<DiscoveredPosting>();
            await using (var gz = new GZipStream(File.OpenRead(await FileAsync(today, ct)), CompressionMode.Decompress))
                await foreach (var item in JsonItems.ReadAsync(gz, "polozky", ct: ct))
                {
                    if (!any && !NavSource.Fold($"{Cs(item, "pozadovanaProfese")} {Cs(item, "upresnujiciInformace")}").Contains(term, StringComparison.Ordinal)) continue;
                    list.Add(Parse(source, item, towns));
                    if (list.Count >= Max) break;
                }
            foreach (var k in Found.Keys.Where(k => k.Day != today).ToList()) Found.Remove(k);
            Found[(today, term)] = list;
            return list;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Os 6 mil municípios do código oficial (obce.json, 900 KB), guardados em disco por 30 dias: o nome da cidade de cada vaga.</summary>
    private async Task<IReadOnlyDictionary<string, string>> TownsAsync(CancellationToken ct)
    {
        var path = Path.Combine(AppPaths.In(AppPaths.Data, "sources", "mpsv"), "obce.json");
        try
        {
            if (!File.Exists(path) || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromDays(30))
                await File.WriteAllBytesAsync(path, await Client.GetByteArrayAsync("https://data.mpsv.cz/od/soubory/ciselniky/obce.json", ct), ct);
            using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(path, ct));
            return doc.RootElement.Obj("polozky").EnumerateArray().Select(o => (Id: o.Str("id"), Name: o.Obj("nazev").Str("cs")))
                .Where(o => o.Id.Length > 0 && o.Name.Length > 0).DistinctBy(o => o.Id).ToDictionary(o => o.Id, o => o.Name);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException) { return new Dictionary<string, string>(); }
    }

    /// <summary>O .gz do dia no diretório de dados; se o download falhar, vale o de ontem.</summary>
    private async Task<string> FileAsync(DateOnly today, CancellationToken ct)
    {
        var folder = AppPaths.In(AppPaths.Data, "sources", "mpsv");
        var path = Path.Combine(folder, $"volna-mista-{today:yyyy-MM-dd}.json.gz");
        if (File.Exists(path)) return path;
        try
        {
            using var res = await Client.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();
            var temp = path + ".parte";
            await using (var file = File.Create(temp)) await res.Content.CopyToAsync(file, ct);
            File.Move(temp, path, true);
            foreach (var old in Directory.EnumerateFiles(folder, "volna-mista-*.json.gz").Where(f => f != path)) File.Delete(old);
            return path;
        }
        catch (HttpRequestException) when (Directory.EnumerateFiles(folder, "volna-mista-*.json.gz").Order().LastOrDefault() is { } yesterday) { return yesterday; }
    }

    internal static DiscoveredPosting Parse(string source, JsonElement v, IReadOnlyDictionary<string, string>? towns = null)
    {
        var id = Num(v, "portalId");
        var site = v.Obj("mistoVykonuPrace");
        var places = site.Obj("pracoviste");
        var place = places.ValueKind == JsonValueKind.Array && places.GetArrayLength() > 0 ? places[0] : default;
        var address = place.ValueKind == JsonValueKind.Object ? place.Obj("adresa") : default;
        var code = site.Obj("obec").Str("id") is { Length: > 0 } o ? o : address.Obj("obec").Str("id");
        var town = towns is not null && towns.TryGetValue(code, out var name) ? name : address.Str("dodatekAdresy");
        var region = Regions.GetValueOrDefault(address.Obj("kraj").Str("id"), "");
        var where = string.Join(", ", new[] { town, region.Contains(town.Length > 0 ? town : "\u0000", StringComparison.OrdinalIgnoreCase) ? "" : region, "Czechia" }.Where(x => x.Length > 0));
        var from = Num(v, "mesicniMzdaOd");
        var to = Num(v, "mesicniMzdaDo");
        var pay = from.Length > 0 || to.Length > 0 ? $"CZK {(from.Length > 0 ? from : to)}{(to.Length > 0 && from.Length > 0 && to != from ? "-" + to : "")}/mês" : null;
        var facts = new List<string> { $"Vaga do registro oficial do Úřad práce (MPSV), profissão {Cs(v, "pozadovanaProfese")}" };
        if (pay is not null) facts.Add($"salário {pay}");
        if (Num(v, "pocetHodinTydne") is { Length: > 0 } hours) facts.Add($"{hours} horas por semana");
        if (v.Str("terminZahajeniPracovnihoPomeru") is { Length: > 0 } start) facts.Add($"início em {start}");
        if (Bool(v, "cizinecMimoEu")) facts.Add("a vaga aceita estrangeiro de fora da UE");
        if (Bool(v, "zamestnaneckaKarta")) facts.Add("vale para o cartão de funcionário tcheco (zaměstnanecká karta), a autorização de trabalho e residência de quem vem de fora da UE");
        if (Bool(v, "modraKarta")) facts.Add("vale para o Cartão Azul da UE (EU Blue Card)");
        var text = $"{Redact(Cs(v, "upresnujiciInformace"))}\n\n{string.Join("; ", facts)}.".Trim();
        var raw = JsonSerializer.Serialize(new
        {
            portalId = id, referencniCislo = v.Str("referencniCislo"), profese = Cs(v, "pozadovanaProfese"), isco = v.Obj("profeseCzIsco").Str("id"),
            zamestnavatel = v.Obj("zamestnavatel").Str("nazev"), misto = town, mzdaOd = from, mzdaDo = to, cizinecMimoEu = Bool(v, "cizinecMimoEu"),
            zamestnaneckaKarta = Bool(v, "zamestnaneckaKarta"), modraKarta = Bool(v, "modraKarta"), datumVlozeni = v.Str("datumVlozeni"), datumZmeny = v.Str("datumZmeny"),
        });
        var kind = v.Obj("pracovnePravniVztahy") is { ValueKind: JsonValueKind.Array } r && r.GetArrayLength() > 0 ? r[0].Str("id") : "";
        return new DiscoveredPosting(source, id, v.Obj("zamestnavatel").Str("nazev"), Cs(v, "pozadovanaProfese"), where,
            $"https://www.uradprace.cz/web/cz/vyhledani-volneho-mista#/volna-mista-detail/{id}", text, v.Date("datumZmeny") ?? v.Date("datumVlozeni"), raw,
            Compensation: pay, Employment: kind.EndsWith("/plny", StringComparison.Ordinal) ? "Tempo integral" : kind.EndsWith("/zkraceny", StringComparison.Ordinal) ? "Meio período" : null,
            ListedAsOpen: true);
    }

    /// <summary>Contato de pessoa (nome depois de "Kontakt", telefone, e-mail) sai do texto: o conjunto avisa que tem dados pessoais.</summary>
    internal static string Redact(string s) => Spaces().Replace(Personal().Replace(s, " "), " ").Trim(' ', ',', ';');

    private static string Cs(JsonElement v, string name) => v.Obj(name).Str("cs");
    private static bool Bool(JsonElement v, string name) => v.TryGetProperty(name, out var b) && b.ValueKind == JsonValueKind.True;
    private static string Num(JsonElement v, string name) =>
        v.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number ? n.GetDecimal().ToString("0.##", CultureInfo.InvariantCulture) : "";

    [GeneratedRegex(@"(?i)\bkontakt(n[ií] osoba)?\b[^.\n;]*|\+?\d[\d \-/]{7,}\d|[\w.+-]+@[\w-]+(\.[\w-]+)+")] private static partial Regex Personal();
    [GeneratedRegex(@"[ \t]{2,}")] private static partial Regex Spaces();
}

/// <summary>
/// Letônia (nva:termo): o conjunto "Vakances" da Agência Estatal de Emprego (NVA) no portal de dados abertos data.gov.lv, CC0, em CSV
/// trocado todo dia. O endereço do arquivo muda com a data: vem do próprio catálogo (CKAN package_show). O CSV não traz o nome da empresa,
/// só o número de registro; a página oficial de cada vaga (cvvp.nva.gov.lv) é o link de candidatura.
/// </summary>
internal sealed class NvaSource(IHttpClientFactory http, TimeProvider time) : PublicSource(http, "nva", "data.gov.lv", "cvvp.nva.gov.lv")
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static (DateOnly Day, string[] Head, List<string[]> Rows)? cache;
    public override string Id => "nva-vakances";
    public override string Name => "NVA vakances (data.gov.lv)";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var term = NavSource.Fold(Key(source));
        var any = term is "" or "*" or "all";
        var (head, rows) = await RowsAsync(ct);
        return [.. rows.Where(r => any || NavSource.Fold($"{Get(head, r, "Vakances_nosaukums")} {Get(head, r, "Vakances_kategorija")}").Contains(term, StringComparison.Ordinal))
            .OrderByDescending(r => Get(head, r, "Aktualizacijas_datums"), StringComparer.Ordinal).Take(300).Select(r => Parse(source, head, r))];
    }

    private async Task<(string[] Head, List<string[]> Rows)> RowsAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        await Gate.WaitAsync(ct);
        try
        {
            if (cache is { } c && c.Day == today) return (c.Head, c.Rows);
            using var meta = await Json.GetAsync(Client, "https://data.gov.lv/dati/api/3/action/package_show?id=vakances", ct);
            var url = meta.RootElement.Obj("result").Obj("resources").EnumerateArray().Select(r => r.Str("url"))
                .First(u => u.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) && u.StartsWith("https://data.gov.lv/", StringComparison.OrdinalIgnoreCase));
            var (head, rows) = ParseCsv(await Client.GetStringAsync(url, ct));
            cache = (today, head, rows);
            return (head, rows);
        }
        finally { Gate.Release(); }
    }

    internal static (string[] Head, List<string[]> Rows) ParseCsv(string csv)
    {
        var lines = csv.TrimStart('﻿').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var head = Leads.CsvLine(lines[0].TrimEnd('\r')).Select(h => h.Trim()).ToArray();
        return (head, [.. lines.Skip(1).Select(l => Leads.CsvLine(l.TrimEnd('\r')).ToArray()).Where(r => r.Length >= head.Length - 1)]);
    }

    internal static DiscoveredPosting Parse(string source, string[] head, string[] r)
    {
        string G(string n) => Get(head, r, n);
        var from = Money(G("Alga_no"));
        var to = Money(G("Alga_lidz"));
        var pay = from.Length > 0 ? $"EUR {from}{(to.Length > 0 && to != from ? "-" + to : "")}/mês" : null;
        var shape = string.Join(", ", new[] { G("Slodzes_tips"), G("Darba_laika_veids") }.Where(x => x.Length > 0));
        var text = $"Vaga do registro oficial da Agência Estatal de Emprego da Letônia (NVA). Área: {G("Vakances_kategorija")}." +
            (pay is null ? "" : $" Salário bruto: {pay}.") + (shape.Length > 0 ? $" {shape}." : "") + (G("Pieteiksanas_termins") is { Length: > 0 } until ? $" Inscrições até {until}." : "");
        var url = G("Vakances_paplasinats_apraksts") is { Length: > 8 } u && u.StartsWith("https://cvvp.nva.gov.lv/", StringComparison.OrdinalIgnoreCase) ? u : "https://cvvp.nva.gov.lv/#/pub/vakances";
        var raw = JsonSerializer.Serialize(head.Select((h, i) => (h, v: i < r.Length ? r[i] : "")).Where(x => x.h.Length > 0).ToDictionary(x => x.h, x => x.v));
        return new DiscoveredPosting(source, G("Vakances_Nr"), $"Empregador {G("Iestades_registracijas_numurs")} (registro da Letônia)", Title(G("Vakances_nosaukums")),
            G("Vieta") is { Length: > 0 } where ? $"{where}, Latvia" : "Latvia", url, text,
            DateTime.TryParse(G("Aktualizacijas_datums"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null, raw,
            Compensation: pay, Employment: G("Slodzes_tips") is { Length: > 0 } s ? s : null, ListedAsOpen: true);
    }

    private static string Get(string[] head, string[] r, string name) => Array.IndexOf(head, name) is var i && i >= 0 && i < r.Length ? r[i].Trim() : "";
    private static string Money(string s) => decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var m) && m > 0 ? m.ToString("0.##", CultureInfo.InvariantCulture) : "";

    /// <summary>"AUTOMOBIĻA VADĪTĀJS" vira "Automobiļa Vadītājs"; título que já vem em caixa mista fica como está.</summary>
    internal static string Title(string s) => s.Any(char.IsLower) ? s.Trim() : CultureInfo.GetCultureInfo("lv-LV").TextInfo.ToTitleCase(s.Trim().ToLower(CultureInfo.GetCultureInfo("lv-LV")));
}

/// <summary>Pinpoint (pinpoint:empresa): o postings.json público de cada quadro, o feed que o próprio Pinpoint oferece para sites de carreira.</summary>
internal sealed class PinpointSource(IHttpClientFactory http) : PublicSource(http, "pinpoint", "pinpointhq.com")
{
    public override string Id => "pinpoint-board";
    public override string Name => "Pinpoint";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var slug = Key(source).ToLowerInvariant();
        using var doc = await Json.GetAsync(Client, $"https://{Uri.EscapeDataString(slug)}.pinpointhq.com/postings.json", ct);
        var data = doc.RootElement.Obj("data");
        return data.ValueKind != JsonValueKind.Array ? [] : [.. data.EnumerateArray().Select(p => Parse(source, slug, p))];
    }

    internal static DiscoveredPosting Parse(string source, string slug, JsonElement p)
    {
        var loc = p.Obj("location");
        var where = string.Join(", ", new[] { loc.Str("city"), loc.Str("province") }.Where(x => x.Length > 0 && !x.StartsWith("Varies", StringComparison.OrdinalIgnoreCase)));
        var mode = p.Str("workplace_type") switch { "remote" => "remote", "hybrid" => "hybrid", "onsite" => "onsite", _ => null };
        string? pay = null;
        if (p.TryGetProperty("compensation_visible", out var visible) && visible.ValueKind == JsonValueKind.True && p.TryGetProperty("compensation_minimum", out var min) && min.ValueKind == JsonValueKind.Number)
            pay = $"{p.Str("compensation_currency")} {min.GetDecimal():0}{(p.TryGetProperty("compensation_maximum", out var max) && max.ValueKind == JsonValueKind.Number ? $"-{max.GetDecimal():0}" : "")}/{p.Str("compensation_frequency")}".Trim();
        var parts = new[] { "description", "key_responsibilities", "skills_knowledge_expertise", "benefits" }.Select(k => Json.Plain(p.Str(k))).Where(x => x.Length > 0);
        return new DiscoveredPosting(source, p.Str("id"), Json.Company(slug), p.Str("title"), mode == "remote" && where.Length == 0 ? "Remote" : where, p.Str("url"),
            string.Join("\n\n", parts), null, p.GetRawText(), Workplace: mode, Compensation: pay, Employment: p.Str("employment_type_text") is { Length: > 0 } e ? e : null, ListedAsOpen: true);
    }
}

/// <summary>
/// BambooHR (bamboohr:empresa): a página pública de vagas de cada empresa (empresa.bamboohr.com/careers) responde em JSON; a lista e o detalhe
/// de cada vaga, até 30 por rodada, uma de cada vez. Endereço observado, sem documentação: experimental.
/// </summary>
internal sealed class BambooHrSource(IHttpClientFactory http) : PublicSource(http, "bamboohr", "bamboohr.com")
{
    private const int MaxDetails = 30;
    public override string Id => "bamboohr-careers";
    public override string Name => "BambooHR";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var slug = Key(source).ToLowerInvariant();
        var root = $"https://{Uri.EscapeDataString(slug)}.bamboohr.com/careers";
        using var list = await Json.GetAsync(Client, $"{root}/list", ct);
        var items = list.RootElement.Obj("result");
        if (items.ValueKind != JsonValueKind.Array) return [];
        var result = new List<DiscoveredPosting>();
        foreach (var j in items.EnumerateArray().Take(MaxDetails))
        {
            using var detail = await Json.GetAsync(Client, $"{root}/{Uri.EscapeDataString(j.Str("id"))}/detail", ct);
            result.Add(Parse(source, slug, j, detail.RootElement.Obj("result").Obj("jobOpening")));
            await Task.Delay(200, ct);
        }
        return result;
    }

    internal static DiscoveredPosting Parse(string source, string slug, JsonElement j, JsonElement detail)
    {
        var loc = detail.ValueKind == JsonValueKind.Object ? detail.Obj("location") : j.Obj("location");
        var country = loc.Str("addressCountry") is { Length: > 0 } c ? c : j.Obj("atsLocation").Str("country");
        var where = string.Join(", ", new[] { loc.Str("city"), loc.Str("state"), country }.Where(x => x.Length > 0).Distinct());
        var remote = j.TryGetProperty("isRemote", out var r) && r.ValueKind == JsonValueKind.True || j.Str("locationType") == "1";
        var url = detail.Str("jobOpeningShareUrl") is { Length: > 0 } u ? u : $"https://{slug}.bamboohr.com/careers/{j.Str("id")}";
        return new DiscoveredPosting(source, j.Str("id"), Json.Company(slug), j.Str("jobOpeningName").Trim(), remote && where.Length == 0 ? "Remote" : where, url,
            Json.Plain(detail.Str("description")), detail.Date("datePosted"), j.GetRawText(), Workplace: remote ? "remote" : j.Str("locationType") == "2" ? "hybrid" : null,
            Employment: j.Str("employmentStatusLabel") is { Length: > 0 } e ? e : null, ListedAsOpen: true);
    }
}

/// <summary>Recruiterbox, hoje Trakstar Hire (recruiterbox:empresa): a API pública de vagas que o widget de carreiras usa (jsapi.recruiterbox.com). Experimental.</summary>
internal sealed class RecruiterboxSource(IHttpClientFactory http) : PublicSource(http, "recruiterbox", "jsapi.recruiterbox.com", "hire.trakstar.com")
{
    public override string Id => "recruiterbox-openings";
    public override string Name => "Recruiterbox (Trakstar Hire)";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var client = Key(source).ToLowerInvariant();
        using var doc = await Json.GetAsync(Client, $"https://jsapi.recruiterbox.com/v1/openings?client_name={Uri.EscapeDataString(client)}&limit=100", ct);
        var items = doc.RootElement.Obj("objects");
        return items.ValueKind != JsonValueKind.Array ? [] : [.. items.EnumerateArray().Select(o => Parse(source, client, o))];
    }

    internal static DiscoveredPosting Parse(string source, string client, JsonElement o)
    {
        var loc = o.Obj("location");
        var where = string.Join(", ", new[] { loc.Str("city"), loc.Str("state"), loc.Str("country") }.Where(x => x.Length > 0));
        var remote = o.TryGetProperty("allows_remote", out var r) && r.ValueKind == JsonValueKind.True;
        var kind = o.Str("position_type").Replace('_', ' ');
        return new DiscoveredPosting(source, o.Str("id"), Json.Company(client), o.Str("title"), remote && where.Length == 0 ? "Remote" : where, o.Str("hosted_url"),
            Json.Plain(o.Str("description")), null, o.GetRawText(), Workplace: remote ? "remote" : null, Employment: kind.Length > 0 ? kind : null, ListedAsOpen: true);
    }
}

/// <summary>Os itens de um array grande dentro do objeto raiz, um por vez, sem carregar o arquivo inteiro (Utf8JsonReader com buffer que cresce).</summary>
internal static class JsonItems
{
    public static async IAsyncEnumerable<JsonElement> ReadAsync(Stream stream, string arrayName, int bufferSize = 1 << 20, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var buffer = new byte[bufferSize];
        int start = 0, end = 0, phase = 0;
        var state = new JsonReaderState();
        var eof = false;
        while (phase < 3)
        {
            if (start > 0) { Buffer.BlockCopy(buffer, start, buffer, 0, end - start); end -= start; start = 0; }
            if (end == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
            if (!eof) { var n = await stream.ReadAsync(buffer.AsMemory(end), ct); if (n == 0) eof = true; else end += n; }
            var items = Scan(buffer.AsSpan(0, end), eof, ref state, ref phase, arrayName, out var used);
            start = used;
            foreach (var item in items) yield return item;
            if (eof && items.Count == 0 && phase < 3) yield break;
        }
    }

    // Fases: 0 procura a propriedade, 1 espera o início do array, 2 dentro dele, 3 acabou. Item incompleto volta para o ponto de antes dele.
    private static List<JsonElement> Scan(ReadOnlySpan<byte> data, bool final, ref JsonReaderState state, ref int phase, string arrayName, out int used)
    {
        var items = new List<JsonElement>();
        var reader = new Utf8JsonReader(data, final, state);
        while (phase < 3)
        {
            var mark = (int)reader.BytesConsumed;
            var markState = reader.CurrentState;
            if (!reader.Read()) break;
            switch (phase)
            {
                case 0 when reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1 && reader.ValueTextEquals(arrayName): phase = 1; break;
                case 1: phase = reader.TokenType == JsonTokenType.StartArray ? 2 : 0; break;
                case 2 when reader.TokenType == JsonTokenType.EndArray: phase = 3; break;
                case 2 when reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray:
                    var from = (int)reader.TokenStartIndex;
                    if (!reader.TrySkip()) { used = mark; state = markState; return items; }
                    using (var doc = JsonDocument.Parse(data[from..(int)reader.BytesConsumed].ToArray())) items.Add(doc.RootElement.Clone());
                    break;
            }
        }
        used = (int)reader.BytesConsumed;
        state = reader.CurrentState;
        return items;
    }
}
