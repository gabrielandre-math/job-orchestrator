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
