using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace JobOrchestrator.BuildingBlocks;

[Flags]
public enum PostingSignals { None = 0, HiddenText = 1, InvisibleCharacters = 2, Instructions = 4 }

public sealed record PostingContent(string Text, PostingSignals Signals);

/// <summary>Limpa conteúdo da fonte antes do modelo e gera alertas sem reproduzir o trecho descartado.</summary>
public static partial class PostingIntegrity
{
    public static PostingContent Inspect(string content)
    {
        var input = Visible(content);
        var signals = input.Signals;
        var visible = Invisible().Replace(input.Text, "");
        if (visible.Length != input.Text.Length) signals |= PostingSignals.InvisibleCharacters;

        var clean = new StringBuilder(visible.Length);
        foreach (var line in visible.ReplaceLineEndings("\n").Split('\n'))
        {
            var match = Instructions().Match(line);
            if (match.Success) signals |= PostingSignals.Instructions;
            clean.AppendLine(match.Success ? line[..match.Index].TrimEnd() : line);
        }

        return new(clean.ToString().Trim(), signals);
    }

    public static IReadOnlyList<string> Warnings(PostingSignals signals)
    {
        var warnings = new List<string>();
        if (signals.HasFlag(PostingSignals.HiddenText)) warnings.Add("Texto oculto removido do anúncio.");
        if (signals.HasFlag(PostingSignals.InvisibleCharacters)) warnings.Add("Caracteres invisíveis removidos do anúncio.");
        if (signals.HasFlag(PostingSignals.Instructions)) warnings.Add("Um trecho com aparência de instrução para o assistente foi removido. Confira a vaga original.");
        return warnings;
    }

    public static PostingContent Visible(string content)
    {
        // Texto simples pode conter comparações e tipos genéricos. Só parseie fragmentos com marcação reconhecível.
        if (!Markup().IsMatch(content)) return new(WebUtility.HtmlDecode(content), PostingSignals.None);
        var doc = new HtmlDocument { OptionMaxNestedChildNodes = 256 };
        try { doc.LoadHtml(content); }
        catch (InvalidOperationException) { return new("", PostingSignals.HiddenText); }

        var text = new StringBuilder(content.Length);
        var signals = PostingSignals.None;
        var nodes = new Stack<(HtmlNode Node, bool End)>();
        nodes.Push((doc.DocumentNode, false));
        while (nodes.TryPop(out var entry))
        {
            var (node, end) = entry;
            if (end) { text.AppendLine(); continue; }
            if (node is HtmlTextNode plain) { text.Append(WebUtility.HtmlDecode(plain.Text)); continue; }
            if (node is HtmlCommentNode comment)
            {
                if (Instructions().IsMatch(WebUtility.HtmlDecode(comment.Comment))) signals |= PostingSignals.HiddenText | PostingSignals.Instructions;
                continue;
            }
            if (node.Name is "script" or "style" or "head" or "svg" or "template" or "noscript") continue;
            var css = WebUtility.HtmlDecode(node.GetAttributeValue("style", ""));
            if (node.Attributes["hidden"] is not null || HiddenStyle().IsMatch(css)
                || node.GetAttributeValue("aria-hidden", "").Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(node.InnerText)) signals |= PostingSignals.HiddenText;
                continue;
            }
            if (node.Name is "article" or "section" or "div" or "p" or "br" or "li" or "h1" or "h2" or "h3" or "h4" or "tr" or "blockquote" or "pre")
            {
                text.AppendLine();
                nodes.Push((node, true));
            }
            for (var i = node.ChildNodes.Count - 1; i >= 0; i--) nodes.Push((node.ChildNodes[i], false));
        }
        return new(text.ToString().Trim(), signals);
    }

    [GeneratedRegex(@"<!--|</?(?:html|head|body|article|section|main|div|span|p|br|li|ul|ol|h[1-6]|script|style|noscript|template|svg|a|strong|b|em|table|pre|blockquote)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Markup();

    [GeneratedRegex(@"(?:^|;)\s*(?:(?:display\s*:\s*none|visibility\s*:\s*(?:hidden|collapse))\b|(?:opacity|font-size)\s*:\s*0(?:\.0+)?(?:px|em|rem|%)?\s*(?:!important\s*)?(?:;|$))", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenStyle();

    [GeneratedRegex("[\u00AD\u200B\u200E\u200F\u202A-\u202E\u2060\u2066-\u2069\uFEFF]")]
    private static partial Regex Invisible();

    [GeneratedRegex(@"\b(?:ignore|disregard|forget|desconsidere|esque[cç]a)\s+(?:(?:all|any|the|todas?|todos?|as|os|a|o)\s+){0,4}(?:(?:previous|prior|above|system)\s+(?:instructions?|prompts?|messages?)|(?:instru[cç][õo]es|regras|mensagens)\s+(?:anteriores?|acima|do\s+sistema))\b|\[(?:/?INST|SYSTEM)\]", RegexOptions.IgnoreCase)]
    private static partial Regex Instructions();
}
