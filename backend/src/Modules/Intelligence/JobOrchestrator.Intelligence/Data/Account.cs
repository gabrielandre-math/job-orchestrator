namespace JobOrchestrator.Intelligence.Data;

/// <summary>
/// Uma conta de IA conectada. Kind: claude-cli e codex-cli (assinatura, login pelo navegador, pasta própria por conta),
/// anthropic-api, openai-api, gemini-api e openai-compatible (chave de API no gnome-keyring, nunca aqui).
/// Position é a ordem de gasto. Chave de API cobra por uso: só liga com teto de tokens por mês.
/// </summary>
internal sealed class Account
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public bool Enabled { get; set; } = true;
    public string? ConfigDir { get; set; }
    public string? BaseUrl { get; set; }
    public string Models { get; set; } = "{}";
    public long? MonthlyTokens { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool Paid => Kind is "anthropic-api" or "openai-api" or "gemini-api" or "openai-compatible";
    public Dictionary<string, string> ModelMap => JsonSerializer.Deserialize<Dictionary<string, string>>(Models) ?? [];
}
