using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace JobOrchestrator.Profile.Import;

/// <summary>Texto de um documento. Pages separadas por form feed (\f), como o pdftotext entrega.</summary>
internal sealed record ExtractedText(string Kind, string Text, int Pages, IReadOnlyDictionary<string, IReadOnlyList<string[]>>? Tables = null);

/// <summary>
/// Tira o texto do documento sem IA: PDF pelo pdftotext (poppler), DOCX lendo o XML do Word, texto puro como veio e o
/// export do LinkedIn (ZIP com CSVs) como tabelas. Nada sai da máquina nesta etapa.
/// </summary>
internal static class TextExtractor
{
    public const int MaxBytes = 15 * 1024 * 1024;

    public static async Task<ExtractedText> ExtractAsync(string path, string fileName, CancellationToken ct)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => await PdfAsync(path, ct),
            ".docx" => Docx(path),
            ".zip" => LinkedIn(path),
            ".txt" or ".md" => new ExtractedText("text", await File.ReadAllTextAsync(path, ct), 1),
            _ => throw new NotSupportedException("Formato não suportado. Envie PDF, DOCX, TXT ou o ZIP do LinkedIn.")
        };
    }

    private static async Task<ExtractedText> PdfAsync(string path, CancellationToken ct)
    {
        // Sem -layout: a ordem de leitura do PDF mantém colunas separadas, e os trechos citados batem.
        var text = await RunAsync("pdftotext", ["-enc", "UTF-8", "-eol", "unix", path, "-"], ct);
        var pages = text.Split('\f').Length;
        if (text.EndsWith('\f')) pages--;
        if (string.IsNullOrWhiteSpace(text.Replace("\f", "")))
            throw new NotSupportedException("O PDF não tem texto (parece imagem escaneada). Exporte o CV como PDF com texto ou envie o DOCX.");
        return new ExtractedText("pdf", text.TrimEnd('\f'), Math.Max(1, pages));
    }

    private static ExtractedText Docx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml") ?? throw new NotSupportedException("O DOCX não tem o corpo do documento.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var sb = new StringBuilder();
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element && reader.NodeType != XmlNodeType.EndElement) continue;
            switch (reader.LocalName)
            {
                case "t" when reader.NodeType == XmlNodeType.Element: sb.Append(reader.ReadElementContentAsString()); break;
                case "tab" when reader.NodeType == XmlNodeType.Element: sb.Append('\t'); break;
                case "br" when reader.NodeType == XmlNodeType.Element: sb.Append('\n'); break;
                case "p" when reader.NodeType == XmlNodeType.EndElement: sb.Append('\n'); break;
            }
        }
        return new ExtractedText("docx", sb.ToString(), 1);
    }

    /// <summary>Export do LinkedIn: Profile, Positions, Education, Skills, Languages, Certifications, Projects e contatos.</summary>
    private static ExtractedText LinkedIn(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var wanted = new[] { "Profile.csv", "Positions.csv", "Education.csv", "Skills.csv", "Languages.csv", "Certifications.csv", "Projects.csv", "Email Addresses.csv", "PhoneNumbers.csv" };
        var tables = new Dictionary<string, IReadOnlyList<string[]>>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        foreach (var name in wanted)
        {
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(Path.GetFileName(e.FullName), name, StringComparison.OrdinalIgnoreCase));
            if (entry is null) continue;
            using var r = new StreamReader(entry.Open(), Encoding.UTF8);
            var rows = Csv.Parse(r.ReadToEnd());
            tables[name] = rows;
            sb.Append("## ").Append(name).Append('\n');
            foreach (var row in rows) sb.Append(string.Join(" | ", row)).Append('\n');
        }
        if (!tables.ContainsKey("Positions.csv") && !tables.ContainsKey("Profile.csv"))
            throw new NotSupportedException("O ZIP não parece o export do LinkedIn (faltam Profile.csv e Positions.csv).");
        return new ExtractedText("linkedin", sb.ToString(), 1, tables);
    }

    private static async Task<string> RunAsync(string file, IEnumerable<string> args, CancellationToken ct)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info) ?? throw new InvalidOperationException($"{file} não iniciou.");
        var output = p.StandardOutput.ReadToEndAsync(ct);
        var error = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) throw new NotSupportedException($"Não foi possível ler o PDF: {(await error).Trim()}");
        return await output;
    }
}

/// <summary>CSV do jeito do LinkedIn: aspas, vírgula e quebra de linha dentro de aspas; linhas "Notes:" do topo ficam de fora.</summary>
internal static class Csv
{
    public static IReadOnlyList<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': quoted = true; break;
                case ',': row.Add(cell.ToString()); cell.Clear(); break;
                case '\r': break;
                case '\n': row.Add(cell.ToString()); cell.Clear(); rows.Add([.. row]); row.Clear(); break;
                default: cell.Append(c); break;
            }
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add([.. row]); }
        var start = rows.FindIndex(r => r.Length > 1);
        return start < 0 ? [] : rows.Skip(start).Where(r => r.Any(x => x.Length > 0)).ToList();
    }
}
