using System.IO.Compression;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Profile.Import;

/// <summary>Um ZIP do export do LinkedIn achado na pasta de downloads.</summary>
internal sealed record FoundExport(string Name, long Bytes, DateTime ModifiedAt);

/// <summary>O LinkedIn ligado ao perfil: o último export lido, quantos fatos vieram dele e os ZIPs prontos para importar.</summary>
internal sealed record LinkedInStatus(bool Connected, string? FileName, DateTime? ImportedAt, string? DocumentStatus, int Facts, IReadOnlyList<FoundExport> Found, string Folder);

/// <summary>
/// Conectar o LinkedIn pelo caminho oficial: a cópia dos dados que o próprio LinkedIn gera (Configurações › Privacidade dos dados ›
/// Obter uma cópia dos seus dados). O app não entra na conta, não usa robô nem cookie: acha o ZIP que você baixou na pasta de downloads
/// (só arquivos com o nome do export) e lê só os CSVs do perfil. Mensagens, conexões, anúncios e o resto nem são abertos nem guardados.
/// </summary>
internal static partial class LinkedInExport
{
    /// <summary>Os CSVs que o app lê; tudo o mais fica fora do arquivo guardado.</summary>
    public static readonly string[] Files = ["Profile.csv", "Positions.csv", "Education.csv", "Skills.csv", "Languages.csv", "Certifications.csv", "Projects.csv",
        "Email Addresses.csv", "PhoneNumbers.csv"];

    public const long MaxBytes = 1L << 30;

    public static bool Looks(string fileName) => ExportName().IsMatch(fileName);

    /// <summary>A pasta de downloads da pessoa (XDG_DOWNLOAD_DIR, senão ~/Downloads).</summary>
    public static string Downloads()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dirs = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".config"), "user-dirs.dirs");
        if (File.Exists(dirs) && XdgDownload().Match(File.ReadAllText(dirs)) is { Success: true } m)
        {
            var path = m.Groups[1].Value.Replace("$HOME", home, StringComparison.Ordinal);
            if (Directory.Exists(path)) return path;
        }
        return Path.Combine(home, "Downloads");
    }

    /// <summary>Os exports do LinkedIn na pasta (mais novo primeiro). Só o nome, o tamanho e a data: nada é aberto aqui.</summary>
    public static IReadOnlyList<FoundExport> Find(string folder) => !Directory.Exists(folder) ? [] :
        [.. new DirectoryInfo(folder).EnumerateFiles("*.zip", SearchOption.TopDirectoryOnly).Where(f => Looks(f.Name))
            .OrderByDescending(f => f.LastWriteTimeUtc).Take(5).Select(f => new FoundExport(f.Name, f.Length, f.LastWriteTimeUtc))];

    /// <summary>Um ZIP só com os CSVs do perfil, na mesma ordem e com a mesma data de cada um (o mesmo export dá o mesmo arquivo).</summary>
    public static byte[] Slim(Stream zip)
    {
        using var source = new ZipArchive(zip, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var slim = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in Files)
                if (source.Entries.FirstOrDefault(e => string.Equals(Path.GetFileName(e.FullName), name, StringComparison.OrdinalIgnoreCase)) is { } entry)
                {
                    var copy = slim.CreateEntry(name, CompressionLevel.Optimal);
                    copy.LastWriteTime = entry.LastWriteTime;
                    using var from = entry.Open();
                    using var to = copy.Open();
                    from.CopyTo(to);
                }
        return output.ToArray();
    }

    [GeneratedRegex(@"LinkedInDataExport[\w.\- ()]*\.zip$", RegexOptions.IgnoreCase)] private static partial Regex ExportName();
    [GeneratedRegex(@"^\s*XDG_DOWNLOAD_DIR=""([^""]+)""", RegexOptions.Multiline)] private static partial Regex XdgDownload();
}
