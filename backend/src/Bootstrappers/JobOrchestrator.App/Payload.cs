using System.IO.Compression;
using JobOrchestrator.BuildingBlocks;

namespace JobOrchestrator.App;

/// <summary>
/// O instalador do Windows num .exe só: o próprio executável do app com o resto do pacote (painel, coletor, driver do Playwright) num
/// zip colado no fim, seguido do tamanho do zip e da marca JOSETUP1. Com dois cliques, extrai numa pasta temporária e abre o instalador
/// de lá; a cópia do executável sem o zip é idêntica à do pacote. A pasta temporária sai quando o instalador fecha.
/// </summary>
internal static class Payload
{
    private static readonly byte[] Magic = "JOSETUP1"u8.ToArray();
    private const string Prefix = "JobOrchestrator-Setup-";

    public static bool Present(string exe)
    {
        try
        {
            using var f = File.OpenRead(exe);
            if (f.Length < 32) return false;
            f.Seek(-8, SeekOrigin.End);
            var tail = new byte[8];
            f.ReadExactly(tail);
            return tail.AsSpan().SequenceEqual(Magic);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Extrai e abre o instalador extraído; este processo sai logo depois.</summary>
    public static int Run(string exe)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{Prefix}{Launcher.Version}-{Guid.NewGuid():N}"[..(Prefix.Length + Launcher.Version.Length + 9)]);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        using (var f = File.OpenRead(exe))
        {
            f.Seek(-16, SeekOrigin.End);
            var len = new byte[8];
            f.ReadExactly(len);
            var zipLength = BitConverter.ToInt64(len);
            var exeLength = f.Length - 16 - zipLength;
            f.Seek(0, SeekOrigin.Begin);
            using (var plain = File.Create(Path.Combine(dir, Path.GetFileName(Desktop.Exe(dir)))))
                Copy(f, plain, exeLength);
            var zip = Path.Combine(dir, "payload.zip");
            using (var z = File.Create(zip)) Copy(f, z, zipLength);
            ZipFile.ExtractToDirectory(zip, dir, overwriteFiles: true);
            File.Delete(zip);
        }
        return Platform.Detach(Desktop.Exe(dir), ["--setup"]) ? 0 : 1;
    }

    private static void Copy(Stream from, Stream to, long count)
    {
        var buffer = new byte[1 << 20];
        while (count > 0)
        {
            var read = from.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read <= 0) throw new EndOfStreamException("O instalador veio cortado. Baixe de novo.");
            to.Write(buffer, 0, read);
            count -= read;
        }
    }

    /// <summary>Instalador rodando da pasta temporária do Setup.exe: apaga a pasta uns segundos depois de fechar.</summary>
    public static void CleanupLater()
    {
        var home = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!OperatingSystem.IsWindows() || !Path.GetFileName(home).StartsWith(Prefix, StringComparison.Ordinal) || !home.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) return;
        var info = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, Arguments = $"/d /c ping -n 6 127.0.0.1 >nul & rmdir /s /q \"{home}\"" };
        try { using var _ = System.Diagnostics.Process.Start(info); } catch (System.ComponentModel.Win32Exception) { }
    }
}
