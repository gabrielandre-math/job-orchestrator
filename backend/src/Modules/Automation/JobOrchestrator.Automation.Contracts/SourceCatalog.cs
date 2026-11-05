using System.Text;
using System.Text.Json;

namespace JobOrchestrator.Automation.Contracts;

/// <summary>Uma linha da auditoria dos 249 países e territórios ISO 3166-1 (CSV do usuário), com o continente pelo geoesquema da ONU.</summary>
public sealed record CountryAudit(string Iso2, string Iso3, string Name, int Portals, int ApiCandidates, int Wapes, string Research, string[] RecordIds, string Continent);

/// <summary>
/// O catálogo V2 do usuário (321 registros de origem e referência) e a auditoria dos 249 países, embutidos no binário a partir de data/catalog.
/// Registro do catálogo não é fonte funcionando; país sem fonte pesquisada continua UNRESEARCHED, sem URL inventada.
/// </summary>
public static class SourceCatalog
{
    private static readonly Lazy<JsonDocument> Doc = new(() =>
    {
        using var stream = typeof(SourceCatalog).Assembly.GetManifestResourceStream("catalogo-fontes-v2.json")!;
        return JsonDocument.Parse(stream);
    });

    private static readonly Lazy<IReadOnlyList<CountryAudit>> Audit = new(() =>
    {
        using var stream = typeof(SourceCatalog).Assembly.GetManifestResourceStream("auditoria-249-paises.csv")!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var rows = new List<CountryAudit>();
        reader.ReadLine();
        while (reader.ReadLine() is { } line)
        {
            if (line.Trim().Length == 0) continue;
            var c = Split(line);
            rows.Add(new CountryAudit(c[0], c[1], c[2], Int(c[3]), Int(c[4]), Int(c[5]), c[6],
                c[7].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), Continent(c[0])));
        }
        return rows;
    });

    public static JsonElement Root => Doc.Value.RootElement;
    public static IEnumerable<JsonElement> Records => Root.GetProperty("sources").EnumerateArray();
    public static IReadOnlyList<CountryAudit> Countries => Audit.Value;

    public static IReadOnlyDictionary<string, string> ContinentNames { get; } = new Dictionary<string, string>
    {
        ["AF"] = "África", ["NA"] = "América do Norte", ["CA"] = "América Central e Caribe", ["SA"] = "América do Sul",
        ["AS"] = "Ásia", ["EU"] = "Europa", ["OC"] = "Oceania", ["AN"] = "Antártida",
    };

    // Geoesquema da ONU (M49), com América Central e Caribe separados; conferido contra os 249 códigos do CSV.
    private static readonly Dictionary<string, string> ByCountry = new Dictionary<string, string>
    {
        ["AF"] = "DZ AO BJ BW BF BI CV CM CF TD KM CG CD CI DJ EG GQ ER SZ ET GA GM GH GN GW KE LS LR LY MG MW ML MR MU YT MA MZ NA NE NG RE RW SH ST SN SC SL SO ZA SS SD TZ TG TN UG EH ZM ZW IO TF",
        ["NA"] = "US CA BM GL PM",
        ["CA"] = "MX GT BZ SV HN NI CR PA AI AG AW BS BB BQ VG KY CU CW DM DO GD GP HT JM MQ MS PR BL KN LC MF VC SX TT TC VI",
        ["SA"] = "AR BO BR CL CO EC FK GF GY PY PE SR UY VE GS BV",
        ["AS"] = "AF AM AZ BH BD BT BN KH CN CY GE HK IN ID IR IQ IL JP JO KZ KW KG LA LB MO MY MV MN MM NP KP OM PK PS PH QA SA SG KR LK SY TW TJ TH TL TR TM AE UZ VN YE",
        ["EU"] = "AX AL AD AT BY BE BA BG HR CZ DK EE FO FI FR DE GI GR GG VA HU IS IE IM IT JE LV LI LT LU MT MD MC ME NL MK NO PL PT RO RU SM RS SK SI ES SJ SE CH UA GB",
        ["OC"] = "AS AU CK FJ PF GU HM KI MH FM NR NC NZ NU NF MP PW PG PN WS SB TK TO TV UM VU WF CC CX",
        ["AN"] = "AQ",
    }.SelectMany(kv => kv.Value.Split(' ').Select(c => (c, kv.Key))).ToDictionary(x => x.c, x => x.Key);

    public static string Continent(string iso2) => ByCountry.GetValueOrDefault(iso2.ToUpperInvariant(), "");

    /// <summary>
    /// Os países que um escopo cobre: GLOBAL (todos), LATAM (América Latina e Caribe), EUROPE, nomes do catálogo (UK, USA, INDIA) e códigos ISO.
    /// Escopo desconhecido não cobre nada.
    /// </summary>
    public static IReadOnlySet<string> Expand(string scope)
    {
        var s = scope.Trim().ToUpperInvariant();
        return s switch
        {
            "GLOBAL" or "WORLDWIDE" => ByCountry.Keys.ToHashSet(),
            "LATAM" => ByCountry.Where(kv => kv.Value is "SA" or "CA").Select(kv => kv.Key).ToHashSet(),
            "EUROPE" or "EU" => ByCountry.Where(kv => kv.Value == "EU").Select(kv => kv.Key).ToHashSet(),
            "UK" => ["GB"], "USA" => ["US"], "INDIA" => ["IN"],
            _ when s.Length == 2 && ByCountry.ContainsKey(s) => [s],
            _ => new HashSet<string>(),
        };
    }

    private static int Int(string s) => int.TryParse(s, out var v) ? v : 0;

    private static string[] Split(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else quoted = !quoted; }
            else if (ch == ',' && !quoted) { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString());
        while (fields.Count < 9) fields.Add("");
        return [.. fields];
    }
}
