using System.Text;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Profile.Import;

/// <summary>
/// Procura trechos citados no texto (o mesmo, já sem dados pessoais, que o modelo leu). A comparação ignora caixa, acento,
/// marcadores de lista e espaços; numa segunda tentativa, também a pontuação. Achou: o fato é do documento.
/// </summary>
internal sealed partial class Evidence
{
    private readonly string[] _pages;
    private readonly int[] _starts;
    private readonly string _all;
    private readonly string _loose;

    public Evidence(string text)
    {
        _pages = [.. text.Split('\f').Select(Fold)];
        _starts = new int[_pages.Length];
        for (int i = 0, at = 0; i < _pages.Length; i++) { _starts[i] = at; at += _pages[i].Length + 1; }
        _all = string.Join(' ', _pages);
        _loose = Loose(_all);
    }

    public string Text => _all;

    /// <summary>Posição do trecho no texto e a página (começa em 1). Nulo se não achou.</summary>
    public (int Position, int Page)? Find(string? quote)
    {
        if (string.IsNullOrWhiteSpace(quote)) return null;
        var q = Fold(quote);
        if (q.Length < 3) return null;
        var at = _all.IndexOf(q, StringComparison.Ordinal);
        if (at < 0)
        {
            var lq = Loose(q);
            if (lq.Length < 3) return null;
            var la = _loose.IndexOf(lq, StringComparison.Ordinal);
            if (la < 0) return null;
            // Posição aproximada no texto normal: proporção, só para achar a página e a janela.
            at = (int)((long)la * _all.Length / Math.Max(1, _loose.Length));
        }
        var page = Array.FindLastIndex(_starts, s => s <= at) + 1;
        return (at, page);
    }

    /// <summary>O termo aparece em algum lugar do documento (palavra inteira).</summary>
    public bool Contains(string term) => Has(_all, Fold(term));

    /// <summary>O termo aparece entre as posições (seção de uma experiência).</summary>
    public bool ContainsIn(string term, int from, int to)
    {
        from = Math.Clamp(from, 0, _all.Length);
        to = Math.Clamp(to, from, _all.Length);
        return Has(_all[from..to], Fold(term));
    }

    public string Window(int position, int radius)
    {
        var from = Math.Max(0, position - radius);
        var to = Math.Min(_all.Length, position + radius);
        return _all[from..to];
    }

    private static bool Has(string haystack, string term)
    {
        if (term.Length == 0) return false;
        var before = term.StartsWith('.') ? "" : @"(?<![\p{L}\p{N}])";
        return Regex.IsMatch(haystack, before + Regex.Escape(term) + @"(?![\p{L}\p{N}#+])");
    }

    public static string Fold(string s) => Claims.Fold(Bullets().Replace(s, " "));

    private static string Loose(string folded)
    {
        var sb = new StringBuilder(folded.Length);
        foreach (var c in folded) sb.Append(char.IsLetterOrDigit(c) || c is '#' or '+' ? c : ' ');
        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"[•●▪◦·▸►✓✔■□➢➤]")] private static partial Regex Bullets();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}
