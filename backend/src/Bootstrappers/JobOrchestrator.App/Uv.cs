using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using JobOrchestrator.BuildingBlocks;

namespace JobOrchestrator.App;

/// <summary>
/// O uv (Astral, MIT/Apache-2.0), que traz um Python só do coletor em qualquer sistema: a versão fica fixa aqui, com o SHA-256 de cada
/// pacote publicado no GitHub, e mora em &lt;dados&gt;/tools. O Python que ele baixa também fica lá, longe do Python do sistema.
/// </summary>
internal static class Uv
{
    public const string Version = "0.13.7";

    private static readonly Dictionary<string, string> Sha256 = new()
    {
        ["aarch64-apple-darwin.tar.gz"] = "81b811b57e4624df7ed3aac148e0359100d9962f3d9dce00aec533dc2e086731",
        ["x86_64-apple-darwin.tar.gz"] = "20c47e4c1f630b8abaff8c291fa9b9aa300fef92c4fbff95adf198b55287cda7",
        ["x86_64-pc-windows-msvc.zip"] = "0cdc5b38fe5a8168ed6a25814f5ba43c21d313dd3187314fccb37b7cec46e021",
        ["aarch64-pc-windows-msvc.zip"] = "505d9e65e7692bac3ddf98c66754aa26c5050c409c184af30a881a0a72cc85e3",
        ["x86_64-unknown-linux-gnu.tar.gz"] = "c5221ff41b7892bfc223543883600125f6a47fe845093e0f5bdff4f359196d83",
        ["aarch64-unknown-linux-gnu.tar.gz"] = "71279f8d3c67241e807b9d8db872d1bf541579fb9f08b7d1eb1682196d1e57dc",
        ["x86_64-unknown-linux-musl.tar.gz"] = "287491da03f2a785c70fa75b13e1f849918c2f2b5415fe84876b8214eeb792f9",
        ["aarch64-unknown-linux-musl.tar.gz"] = "2d3ec076a0c4c0e8250bb6418c44080828b544f1a3be0b6e46adf9fb6923de45",
    };

    private static string Tools => AppPaths.In(AppPaths.Data, "tools");
    public static string Exe => Path.Combine(Tools, $"uv-{Version}", OperatingSystem.IsWindows() ? "uv.exe" : "uv");

    /// <summary>Python gerenciado pelo uv, sempre o mesmo (3.12), com cache e instalação dentro da pasta de dados.</summary>
    public static IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
    {
        ["UV_PYTHON_INSTALL_DIR"] = Path.Combine(Tools, "python"), ["UV_CACHE_DIR"] = Path.Combine(Tools, "uv-cache"),
        ["UV_PYTHON_PREFERENCE"] = "only-managed", ["UV_NO_PROGRESS"] = "1", ["UV_LINK_MODE"] = "copy",
    };

    /// <summary>O pacote deste sistema (musl no Alpine e afins).</summary>
    public static string? Asset()
    {
        var arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        if (!arm && RuntimeInformation.OSArchitecture != Architecture.X64) return null;
        var cpu = arm ? "aarch64" : "x86_64";
        if (OperatingSystem.IsMacOS()) return $"{cpu}-apple-darwin.tar.gz";
        if (OperatingSystem.IsWindows()) return $"{cpu}-pc-windows-msvc.zip";
        if (!OperatingSystem.IsLinux()) return null;
        var musl = RuntimeInformation.RuntimeIdentifier.Contains("musl", StringComparison.Ordinal) || File.Exists("/etc/alpine-release");
        return $"{cpu}-unknown-linux-{(musl ? "musl" : "gnu")}.tar.gz";
    }

    public static async Task<string> EnsureAsync(HttpClient http, Action<string> log, CancellationToken ct)
    {
        if (File.Exists(Exe)) return Exe;
        var asset = Asset() ?? throw new InvalidOperationException("O uv não tem pacote para este sistema.");
        var file = Path.Combine(AppPaths.In(AppPaths.Data, "downloads"), $"uv-{asset}");
        log($"Baixando o uv {Version} ({asset})");
        await using (var input = await http.GetStreamAsync($"https://github.com/astral-sh/uv/releases/download/{Version}/uv-{asset}", ct))
        await using (var output = File.Create(file))
            await input.CopyToAsync(output, ct);
        await using (var s = File.OpenRead(file))
            if (!string.Equals(Convert.ToHexStringLower(await SHA256.HashDataAsync(s, ct)), Sha256[asset], StringComparison.Ordinal))
            {
                File.Delete(file);
                throw new InvalidOperationException("O SHA-256 do uv baixado não confere com o publicado. Nada foi instalado.");
            }
        var staging = Path.Combine(Tools, $"uv-{Version}.tmp");
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        if (asset.EndsWith(".zip", StringComparison.Ordinal)) ZipFile.ExtractToDirectory(file, staging);
        else
        {
            await using var input = File.OpenRead(file);
            await using var gz = new GZipStream(input, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gz, staging, overwriteFiles: true, ct);
        }
        // O .tar.gz traz uma pasta (uv-x86_64-unknown-linux-gnu/uv); o .zip, os executáveis soltos.
        var name = OperatingSystem.IsWindows() ? "uv.exe" : "uv";
        var found = Directory.EnumerateFiles(staging, name, SearchOption.AllDirectories).FirstOrDefault() ?? throw new InvalidOperationException("O pacote do uv veio sem o executável.");
        var dir = Path.GetDirectoryName(Exe)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.Move(Path.GetDirectoryName(found)!, dir);
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        File.Delete(file);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        log("uv pronto (SHA-256 conferido)");
        return Exe;
    }
}
