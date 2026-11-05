using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace JobOrchestrator.BuildingBlocks;

/// <summary>
/// Segredos (senhas de app de e-mail, chaves de API) no chaveiro do sistema: o Secret Service (gnome-keyring, KWallet) pelo
/// secret-tool no Linux, o Chaveiro pelo /usr/bin/security no macOS e o Gerenciador de Credenciais no Windows. Nunca em arquivo,
/// banco ou log, e o segredo nunca vai na linha de comando: entra pelo stdin (Linux e macOS) ou pela API do Windows.
/// </summary>
public static class Secrets
{
    private const string Service = "job-orchestrator";

    /// <summary>Onde os segredos ficam neste sistema, para a tela.</summary>
    public static string StoreName => OperatingSystem.IsMacOS() ? "Chaveiro do macOS" : OperatingSystem.IsWindows() ? "Gerenciador de Credenciais do Windows" : "chaveiro do sistema (Secret Service)";

    public static bool Available => OperatingSystem.IsMacOS() || OperatingSystem.IsWindows() || Platform.Which("secret-tool") is not null;

    public static async Task<bool> StoreAsync(string account, string secret, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows()) return CredentialStore.Write($"{Service}:{account}", account, secret);
        if (OperatingSystem.IsMacOS())
        {
            // security -i lê o comando pelo stdin; a senha vai em hexadecimal (-X), então nenhuma aspa ou espaço dela quebra o comando.
            // Guardada como b64:..., porque o find-generic-password devolve em hexadecimal qualquer senha que não seja ASCII puro.
            var stored = "b64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
            var line = $"add-generic-password -U -s {Service} -a \"{Safe(account)}\" -l \"Job Orchestrator: {Safe(account)}\" -X {Convert.ToHexString(Encoding.UTF8.GetBytes(stored))}\n";
            await RunAsync("/usr/bin/security", ["-i"], line, ct);
            return await GetAsync(account, ct) == secret;
        }
        return (await RunAsync("secret-tool", ["store", "--label", $"Job Orchestrator: {account}", "service", Service, "account", account], secret, ct)).Exit == 0;
    }

    public static async Task<string?> GetAsync(string account, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows()) return CredentialStore.Read($"{Service}:{account}");
        var (exit, output) = OperatingSystem.IsMacOS()
            ? await RunAsync("/usr/bin/security", ["find-generic-password", "-s", Service, "-a", Safe(account), "-w"], null, ct)
            : await RunAsync("secret-tool", ["lookup", "service", Service, "account", account], null, ct);
        var value = output.TrimEnd('\r', '\n');
        if (exit != 0 || value.Trim().Length == 0) return null;
        if (value.StartsWith("b64:", StringComparison.Ordinal))
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value[4..])); } catch (FormatException) { return null; }
        }
        return value.Trim();
    }

    public static async Task ClearAsync(string account, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows()) { CredentialStore.Delete($"{Service}:{account}"); return; }
        if (OperatingSystem.IsMacOS()) await RunAsync("/usr/bin/security", ["delete-generic-password", "-s", Service, "-a", Safe(account)], null, ct);
        else await RunAsync("secret-tool", ["clear", "service", Service, "account", account], null, ct);
    }

    /// <summary>Os nomes de conta são do próprio app (source:adzuna:appId, mail:voce@exemplo.com); aspas e quebras de linha não entram.</summary>
    private static string Safe(string account) =>
        account.IndexOfAny(['"', '\\', '\n', '\r']) >= 0 ? throw new ArgumentException("Nome de conta inválido para o chaveiro.", nameof(account)) : account;

    private static async Task<(int Exit, string Output)> RunAsync(string file, string[] args, string? stdin, CancellationToken ct)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        Process p;
        try { p = Process.Start(info) ?? throw new InvalidOperationException($"{file} não iniciou."); }
        catch (System.ComponentModel.Win32Exception) { return (-1, ""); }
        using (p)
        {
            // Chaveiro trancado (sessão sem tela, como por SSH no Mac) faz o sistema pedir a senha numa janela e o comando esperar:
            // 20 segundos e o processo sai, sem segredo e sem travar o app.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                if (stdin is not null) await p.StandardInput.WriteAsync(stdin.AsMemory(), timeout.Token);
                p.StandardInput.Close();
                var stderr = p.StandardError.ReadToEndAsync(timeout.Token);
                var output = await p.StandardOutput.ReadToEndAsync(timeout.Token);
                await p.WaitForExitAsync(timeout.Token);
                await stderr;
                return (p.ExitCode, output);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return (-1, "");
            }
        }
    }
}

/// <summary>Credenciais genéricas do Windows (advapi32): por usuário, guardadas pelo próprio sistema e visíveis no Gerenciador de Credenciais.</summary>
[SupportedOSPlatform("windows")]
internal static class CredentialStore
{
    private const uint Generic = 1;
    private const uint PersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CredFree(IntPtr buffer);

    public static bool Write(string target, string user, string secret)
    {
        var bytes = Encoding.Unicode.GetBytes(secret);
        if (bytes.Length > 2560) return false;
        var blob = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential { Type = Generic, TargetName = target, UserName = user, CredentialBlob = blob, CredentialBlobSize = (uint)bytes.Length, Persist = PersistLocalMachine };
            return CredWrite(ref credential, 0);
        }
        finally
        {
            // O segredo não fica na memória solta depois de gravado.
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeHGlobal(blob);
        }
    }

    public static string? Read(string target)
    {
        if (!CredRead(target, Generic, 0, out var ptr)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(ptr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero) return null;
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally { CredFree(ptr); }
    }

    public static void Delete(string target) => CredDelete(target, Generic, 0);
}
