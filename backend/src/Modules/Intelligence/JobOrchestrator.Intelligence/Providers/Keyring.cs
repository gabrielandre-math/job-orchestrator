namespace JobOrchestrator.Intelligence.Providers;

/// <summary>Chaves de API no chaveiro do sistema (Secrets): Secret Service no Linux, Chaveiro no macOS, Gerenciador de Credenciais no Windows.</summary>
internal static class Keyring
{
    public static Task<bool> StoreAsync(string account, string secret, CancellationToken ct) => Secrets.StoreAsync(account, secret, ct);

    public static Task<string?> GetAsync(string account, CancellationToken ct) => Secrets.GetAsync(account, ct);

    public static Task ClearAsync(string account, CancellationToken ct) => Secrets.ClearAsync(account, ct);
}
