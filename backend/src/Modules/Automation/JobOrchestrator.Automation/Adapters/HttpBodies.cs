using System.Text.Json;
using System.Xml.Linq;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Corpo de resposta lido direto do fluxo, sem virar texto inteiro na memória (documentação do .NET sobre objetos grandes): o JSON e
/// o XML das fontes passam de centenas de KB e, como string, ocupariam a área de objetos grandes duas vezes, no texto e na cópia em UTF-8.
/// </summary>
internal static class HttpBodies
{
    public static async Task<JsonDocument> JsonAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    public static async Task<JsonDocument> JsonAsync(HttpClient client, string url, CancellationToken ct)
    {
        await using var stream = await client.GetStreamAsync(url, ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    public static Task<XDocument> XmlAsync(HttpClient client, string url, CancellationToken ct) => XmlAsync(client, new Uri(url), ct);

    public static async Task<XDocument> XmlAsync(HttpClient client, Uri url, CancellationToken ct)
    {
        await using var stream = await client.GetStreamAsync(url, ct);
        return await XDocument.LoadAsync(stream, LoadOptions.None, ct);
    }
}
