using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace JobOrchestrator.Materials.Compose;

internal sealed record LayoutLine(double Top, double Bottom, string Text);
internal sealed record LayoutPage(double Height, IReadOnlyList<LayoutLine> Lines);

/// <summary>Usa a posição do texto no PDF final. Os limiares são avisos para os temas de uma coluna.</summary>
internal static class PdfLayout
{
    private static readonly string[] Sections =
        ["resumo", "competencias", "experiencia", "formacao", "certificacoes", "idiomas",
         "summary", "skills", "experience", "education", "certifications", "languages"];

    public static IReadOnlyList<string> Find(string path, IEnumerable<string> roleTitles)
    {
        try
        {
            using var pdf = PdfDocument.Open(path);
            return Measure(pdf.GetPages().Select(ToLayout).ToArray(), roleTitles);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or PdfDocumentFormatException)
        {
            return ["Não foi possível medir as posições do texto."];
        }
    }

    private static LayoutPage ToLayout(Page page)
    {
        var rows = new List<List<Word>>();
        foreach (var word in page.GetWords().OrderByDescending(x => x.BoundingBox.Bottom))
        {
            if (rows.Count == 0 || rows[^1][0].BoundingBox.Bottom - word.BoundingBox.Bottom > 3) rows.Add([]);
            rows[^1].Add(word);
        }
        return new(page.Height, [.. rows.Select(row => new LayoutLine(
            page.Height - row.Max(x => x.BoundingBox.Top), page.Height - row.Min(x => x.BoundingBox.Bottom),
            string.Join(" ", row.OrderBy(x => x.BoundingBox.Left).Select(x => x.Text))))]);
    }

    internal static IReadOnlyList<string> Measure(IReadOnlyList<LayoutPage> pages, IEnumerable<string> roleTitles)
    {
        var titles = roleTitles.Select(x => Claims.Fold(x).Trim()).Where(x => x.Length > 0).ToArray();
        var findings = new List<string>();
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var rows = page.Lines.OrderBy(x => x.Top).ToArray();
            void Add(string message) => findings.Add($"Página {i + 1}: {message}");
            if (rows.Length == 0) { Add("sem linhas de texto"); continue; }

            var first = rows[0];
            var last = rows[^1];
            if (first.Top < 16 || last.Bottom > page.Height - 16) Add("texto muito perto da margem");
            if (rows.Zip(rows.Skip(1)).Any(pair => pair.Second.Top - pair.First.Bottom > 95))
                Add("espaço vertical grande entre linhas");

            if (i < pages.Count - 1)
            {
                if (last.Bottom < page.Height * .72) Add("termina cedo antes da próxima página");
                var tail = Claims.Fold(last.Text).Trim();
                if (Sections.Contains(tail) || titles.Any(t => tail == t || tail.StartsWith(t + ", ", StringComparison.Ordinal)
                    || tail.StartsWith(t + " ", StringComparison.Ordinal)))
                    Add("título isolado antes da quebra");
            }
            else if (pages.Count > 1 && last.Bottom - first.Top < page.Height * .19)
                Add("última página com pouco texto");
        }
        return findings;
    }
}
