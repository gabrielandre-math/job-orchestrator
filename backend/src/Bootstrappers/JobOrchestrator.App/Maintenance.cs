using JobOrchestrator.BuildingBlocks;
using JobOrchestrator.BuildingBlocks.Data;

namespace JobOrchestrator.App;

/// <summary>Desinstalar e restaurar o banco, pelo terminal ou pela entrada do app nas Configurações do Windows.</summary>
internal static class Maintenance
{
    /// <summary>
    /// Tira o serviço, o atalho e os arquivos do app. Os seus dados (perfil, banco, currículos) ficam, a não ser com --purge: um app
    /// reinstalado depois encontra tudo como estava.
    /// </summary>
    public static async Task<int> UninstallAsync(bool purge)
    {
        await Launcher.StopAsync();
        Desktop.RemoveAutostart();
        Desktop.RemoveShortcut();
        if (File.Exists(Launcher.Marker)) File.Delete(Launcher.Marker);
        var target = Desktop.Target;
        if (OperatingSystem.IsWindows())
        {
            // O executável que roda agora mora na pasta: o cmd apaga depois que este processo sair.
            var info = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, Arguments = $"/d /c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{target}\"" };
            try { using var _ = System.Diagnostics.Process.Start(info); } catch (System.ComponentModel.Win32Exception) { }
        }
        else if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        if (purge)
        {
            foreach (var dir in new[] { AppPaths.Data, AppPaths.Config }) if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            return Launcher.Print("Desinstalado, com os dados apagados.");
        }
        return Launcher.Print($"Desinstalado. Os seus dados ficaram em {AppPaths.Data} e {AppPaths.Config}; com --purge, saem também.");
    }

    /// <summary>Backup agora, com o banco no ar: a cópia física do Postgres próprio em &lt;dados&gt;/backups (a mesma das 7h55).</summary>
    public static async Task<int> BackupAsync()
    {
        if (!PortablePostgres.Installed) return Launcher.Print("O backup por aqui vale para o Postgres próprio; com o Docker, use scripts/backup.sh.") + 1;
        var started = !await Launcher.PortOpenAsync(5462) && await PortablePostgres.StartAsync(Console.WriteLine, CancellationToken.None);
        var cs = new Microsoft.Extensions.Configuration.ConfigurationBuilder().SetBasePath(AppPaths.Resources).AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(Path.Combine(AppPaths.Config, "settings.json"), optional: true).Build().GetConnectionString("Default") ?? "";
        var file = await PortablePostgres.BackupAsync(cs, AppPaths.In(AppPaths.Data, "backups"), CancellationToken.None);
        if (started) await PortablePostgres.StopAsync(CancellationToken.None);
        return Launcher.Print($"Backup em {file} ({new FileInfo(file).Length / 1_000_000.0:0.0} MB).");
    }

    /// <summary>
    /// Conferência rápida do que depende do sistema, sem mexer em nada seu: grava, lê e apaga um segredo de teste no chaveiro, acha o
    /// navegador e mostra as pastas. Serve para validar um sistema novo (e o Wine) antes de instalar.
    /// </summary>
    public static async Task<int> SelfTestAsync()
    {
        var fails = 0;
        void Line(string name, bool ok, string detail) { Console.WriteLine($"{(ok ? "ok  " : "FALHA")} {name}: {detail}"); if (!ok) fails++; }
        Line("sistema", true, $"{Platform.Describe()} ({System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier})");
        Line("pastas", true, $"dados {AppPaths.Data}; configuração {AppPaths.Config}; arquivos do app {AppPaths.Resources}");
        var secret = $"teste-{Guid.NewGuid():N}";
        var stored = await Secrets.StoreAsync("selftest", secret, CancellationToken.None);
        var read = await Secrets.GetAsync("selftest", CancellationToken.None);
        await Secrets.ClearAsync("selftest", CancellationToken.None);
        var gone = await Secrets.GetAsync("selftest", CancellationToken.None) is null;
        Line("chaveiro", stored && read == secret && gone, $"{Secrets.StoreName}: gravar {(stored ? "ok" : "falhou")}, ler {(read == secret ? "ok" : "falhou")}, apagar {(gone ? "ok" : "falhou")}");
        Line("navegador", Platform.Browser() is not null, Platform.Browser() ?? "nenhum Chrome, Edge, Chromium ou Brave");
        var (cpu, total, free) = Machine.Describe();
        Line("máquina", total > 0, $"{cpu}, {total / 1024.0:0.0} GB de RAM, {free / 1024.0:0.0} GB livres");
        Line("postgres portátil", PortablePostgres.Platform() is not null, PortablePostgres.Platform() ?? "sem pacote para este sistema");
        Line("uv", Uv.Asset() is not null, Uv.Asset() ?? "sem pacote para este sistema");
        return fails;
    }

    /// <summary>Troca o banco pelo de um backup (o zip do backup da noite) com tudo parado; o banco de antes fica ao lado.</summary>
    public static async Task<int> RestoreAsync(string zip)
    {
        if (!File.Exists(zip)) return Launcher.Print($"Não achei {zip}.") + 1;
        if (!PortablePostgres.Installed) return Launcher.Print("A restauração vale para o Postgres próprio do app; o do Docker restaura pelo pg_restore.") + 1;
        await Launcher.StopAsync();
        var aside = PortablePostgres.Restore(zip);
        if (!await PortablePostgres.StartAsync(Console.WriteLine, CancellationToken.None)) return Launcher.Print($"Restaurado, mas o Postgres não subiu. Veja {PortablePostgres.Log}; o banco de antes está em {aside}.") + 1;
        return Launcher.Print($"Banco restaurado de {Path.GetFileName(zip)}. O de antes ficou em {aside}. Abra o app para continuar.");
    }
}
