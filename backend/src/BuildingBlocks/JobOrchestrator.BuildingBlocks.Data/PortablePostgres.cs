using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Npgsql;
using System.Text.Json;

namespace JobOrchestrator.BuildingBlocks.Data;

/// <summary>
/// O Postgres que vem com o app quando não há Docker (o caso comum no macOS e no Windows): os binários oficiais do PostgreSQL 17,
/// empacotados por plataforma pelo projeto zonky no Maven Central e conferidos pelo SHA-256 fixado aqui, um cluster em
/// &lt;dados&gt;/postgres/data preso em 127.0.0.1 e ligado e desligado pelo pg_ctl. Mesmo usuário, senha, banco e porta do compose.
/// </summary>
public static class PortablePostgres
{
    public const string Version = "17.11.0";

    /// <summary>SHA-256 do .jar de cada plataforma, do arquivo .sha256 publicado junto no Maven Central.</summary>
    private static readonly Dictionary<string, string> Sha256 = new()
    {
        ["darwin-arm64v8"] = "a1c2786acb0c398f9b2d76806fc52f5dc8b222cbc8e9383a9b9702084daaf3a5",
        ["darwin-amd64"] = "d464ff178e9860ba204662ac23fa547504b7fd392392ff2fb92e3fd73b5bdb64",
        ["windows-amd64"] = "98040fae18dd9633ff95932125b0cecf0a45a1a9312e216e2a33ad03a31d4251",
        ["linux-amd64"] = "0dd7b72b6f335b8ecfb355fa24c5781e8a93edd09880bb77eb52ebbf29b3e96d",
        ["linux-arm64v8"] = "8b042e0ea418b1927d95399207950da0734d27fe01e29503ade3bdc464ad4c2b",
    };

    public static string Root => Path.Combine(AppPaths.Data, "postgres");
    public static string Bin => Path.Combine(Root, Version, "bin");
    public static string Cluster => Path.Combine(Root, "data");
    public static string Log => Path.Combine(Root, "postgres.log");
    public static bool Installed => File.Exists(Path.Combine(Cluster, "PG_VERSION")) && File.Exists(Exe("pg_ctl"));

    private static string Exe(string name) => Path.Combine(Bin, OperatingSystem.IsWindows() ? name + ".exe" : name);

    /// <summary>A plataforma no nome do pacote; o Windows ARM roda o de x64 pela emulação do próprio Windows.</summary>
    public static string? Platform()
    {
        var arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        if (OperatingSystem.IsMacOS()) return arm ? "darwin-arm64v8" : "darwin-amd64";
        if (OperatingSystem.IsWindows()) return "windows-amd64";
        if (OperatingSystem.IsLinux()) return arm ? "linux-arm64v8" : RuntimeInformation.OSArchitecture == Architecture.X64 ? "linux-amd64" : null;
        return null;
    }

    public static string PackageUrl(string platform) =>
        $"https://repo1.maven.org/maven2/io/zonky/test/postgres/embedded-postgres-binaries-{platform}/{Version}/embedded-postgres-binaries-{platform}-{Version}.jar";

    /// <summary>Baixa (uns 60 MB), confere o SHA-256 e extrai os binários. Já extraídos, não faz nada.</summary>
    public static async Task InstallAsync(HttpClient http, Action<string> log, CancellationToken ct)
    {
        if (File.Exists(Exe("pg_ctl")) && File.Exists(Exe("initdb"))) return;
        var platform = Platform() ?? throw new InvalidOperationException($"Sem Postgres portátil para {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}.");
        var downloads = AppPaths.In(AppPaths.Data, "downloads");
        var jar = Path.Combine(downloads, $"postgres-{platform}-{Version}.jar");
        log($"Baixando o PostgreSQL {Version[..Version.LastIndexOf('.')]} ({platform})");
        using (var res = await http.GetAsync(PackageUrl(platform), HttpCompletionOption.ResponseHeadersRead, ct))
        {
            res.EnsureSuccessStatusCode();
            var total = res.Content.Headers.ContentLength ?? 0;
            await using var input = await res.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(jar);
            var buffer = new byte[1 << 20];
            long done = 0, shown = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0 && done - shown > total / 5) { shown = done; log($"{done / 1_000_000} de {total / 1_000_000} MB"); }
            }
        }
        await using (var s = File.OpenRead(jar))
            if (!string.Equals(Convert.ToHexStringLower(await SHA256.HashDataAsync(s, ct)), Sha256[platform], StringComparison.Ordinal))
            {
                File.Delete(jar);
                throw new InvalidOperationException("O SHA-256 do Postgres baixado não confere com o publicado. Nada foi instalado.");
            }
        log("SHA-256 conferido; extraindo");
        var target = Path.Combine(Root, Version);
        var staging = target + ".tmp";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        var txz = Path.Combine(downloads, $"postgres-{platform}-{Version}.txz");
        using (var zip = ZipFile.OpenRead(jar))
            (zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".txz", StringComparison.Ordinal)) ?? throw new InvalidOperationException("Pacote do Postgres sem o .txz.")).ExtractToFile(txz, overwrite: true);
        // O XZ do SharpCompress precisa de um arquivo com posição (não lê direto de dentro do .jar).
        await using (var file = File.OpenRead(txz))
        await using (var xz = new SharpCompress.Compressors.Xz.XZStream(file))
            await TarFile.ExtractToDirectoryAsync(xz, staging, overwriteFiles: true, ct);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.Move(staging, target);
        File.Delete(txz);
        File.Delete(jar);
    }

    /// <summary>
    /// Cria o cluster (initdb com UTF-8, ordenação C e o provedor de locale embutido, igual em qualquer sistema), preso em 127.0.0.1 na
    /// porta da conexão, sem socket Unix, e cria o banco do app. A senha vai por arquivo temporário, nunca pela linha de comando.
    /// </summary>
    public static async Task InitAsync(string connectionString, Action<string> log, CancellationToken ct)
    {
        var cs = new NpgsqlConnectionStringBuilder(connectionString);
        if (!File.Exists(Path.Combine(Cluster, "PG_VERSION")))
        {
            if (Directory.Exists(Cluster) && Directory.EnumerateFileSystemEntries(Cluster).Any())
                throw new InvalidOperationException($"A pasta {Cluster} já tem arquivos que não são um banco. Mova ou apague para criar o banco.");
            Directory.CreateDirectory(Cluster);
            var pwfile = Path.Combine(Root, $".pw-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(pwfile, cs.Password ?? "", ct);
            try
            {
                log("initdb");
                string[] common = ["-D", Cluster, "-U", cs.Username ?? "jo", $"--pwfile={pwfile}", "-E", "UTF8", "--locale=C", "-A", "scram-sha-256", "--no-instructions"];
                var (exit, output) = await RunAsync(Exe("initdb"), [.. common, "--locale-provider=builtin", "--builtin-locale=C.UTF-8"], TimeSpan.FromMinutes(3), ct);
                if (exit != 0)
                {
                    // Sem o provedor embutido (não deveria acontecer no 17), fica a ordenação C do libc.
                    if (Directory.Exists(Cluster)) { Directory.Delete(Cluster, recursive: true); Directory.CreateDirectory(Cluster); }
                    (exit, output) = await RunAsync(Exe("initdb"), common, TimeSpan.FromMinutes(3), ct);
                }
                if (exit != 0) throw new InvalidOperationException($"initdb falhou: {Tail(output)}");
            }
            finally { File.Delete(pwfile); }
            await File.AppendAllTextAsync(Path.Combine(Cluster, "postgresql.conf"), $"""

                # Job Orchestrator: só nesta máquina, sem socket Unix (o caminho estoura o limite no macOS), UTC como o contêiner.
                listen_addresses = '127.0.0.1'
                port = {cs.Port}
                unix_socket_directories = ''
                max_connections = 60
                shared_buffers = 128MB
                max_wal_size = 1GB
                wal_keep_size = 512MB
                timezone = 'UTC'
                log_timezone = 'UTC'
                logging_collector = off

                """, ct);
        }
        if (!await StartAsync(log, ct)) throw new InvalidOperationException($"O Postgres não subiu. Veja {Log}.");
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false };
        await using var conn = new NpgsqlConnection(admin.ConnectionString);
        await conn.OpenAsync(ct);
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", conn);
        exists.Parameters.AddWithValue("name", cs.Database ?? "job_orchestrator");
        if (await exists.ExecuteScalarAsync(ct) is null)
        {
            log($"CREATE DATABASE {cs.Database}");
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{(cs.Database ?? "job_orchestrator").Replace("\"", "", StringComparison.Ordinal)}\"", conn);
            await create.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>pg_ctl start e espera responder. No Windows, o pg_ctl tira os privilégios de administrador do processo sozinho.</summary>
    public static async Task<bool> StartAsync(Action<string>? log, CancellationToken ct)
    {
        if (!Installed) return false;
        // Sobra de uma queda (máquina desligada no meio): o pg_ctl recusa subir com um postmaster.pid de processo que não existe mais.
        var pid = Path.Combine(Cluster, "postmaster.pid");
        if (File.Exists(pid) && int.TryParse(File.ReadLines(pid).FirstOrDefault(), out var old) && !Alive(old)) File.Delete(pid);
        log?.Invoke("pg_ctl start");
        var (exit, output) = await RunAsync(Exe("pg_ctl"), ["start", "-D", Cluster, "-l", Log, "-w", "-t", "120", "-s"], TimeSpan.FromMinutes(3), ct);
        if (exit != 0) log?.Invoke(Tail(output));
        return exit == 0;
    }

    public static async Task StopAsync(CancellationToken ct)
    {
        if (!Installed || !File.Exists(Path.Combine(Cluster, "postmaster.pid"))) return;
        await RunAsync(Exe("pg_ctl"), ["stop", "-D", Cluster, "-m", "fast", "-w", "-t", "60", "-s"], TimeSpan.FromMinutes(2), ct);
    }

    private static bool Alive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return false; }
    }

    /// <summary>Pastas cujo conteúdo o próprio Postgres manda deixar fora de uma cópia física (a WAL entra no fim, depois do pg_backup_stop).</summary>
    private static readonly string[] SkipDirs = ["pg_wal", "pg_replslot", "pg_dynshmem", "pg_notify", "pg_serial", "pg_snapshots", "pg_stat_tmp", "pg_subtrans"];
    private static readonly string[] SkipFiles = ["postmaster.pid", "postmaster.opts", "backup_label", "tablespace_map", "postgresql.auto.conf.tmp"];

    /// <summary>
    /// Backup físico com o banco no ar (pg_backup_start e pg_backup_stop, na mesma sessão): copia a pasta do cluster para um zip,
    /// grava o backup_label que o stop devolve e junta a WAL do período. Restaurar é trocar a pasta data pelo conteúdo de data/ do zip
    /// e subir: o Postgres refaz a WAL e fica consistente (job-orchestrator --restore faz isso).
    /// </summary>
    public static async Task<string> BackupAsync(string connectionString, string dir, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd-HHmm}.pgdata.zip");
        var tmp = file + ".tmp";
        await using var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, CommandTimeout = 0 }.ConnectionString);
        await conn.OpenAsync(ct);
        await using (var start = new NpgsqlCommand("SELECT pg_backup_start('job-orchestrator', true)", conn)) await start.ExecuteNonQueryAsync(ct);
        var stopped = false;
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                foreach (var path in Directory.EnumerateFiles(Cluster, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(Cluster, path).Replace('\\', '/');
                    if (SkipDirs.Contains(rel.Split('/')[0]) || SkipFiles.Contains(rel) || rel.Contains("pgsql_tmp", StringComparison.Ordinal)) continue;
                    await AddAsync(zip, path, "data/" + rel, ct);
                }
                foreach (var empty in SkipDirs.Append("pg_wal/archive_status")) zip.CreateEntry($"data/{empty}/");
                string label, map;
                await using (var stop = new NpgsqlCommand("SELECT labelfile, spcmapfile FROM pg_backup_stop(false)", conn))
                await using (var reader = await stop.ExecuteReaderAsync(ct))
                {
                    await reader.ReadAsync(ct);
                    label = reader.GetString(0);
                    map = reader.IsDBNull(1) ? "" : reader.GetString(1);
                }
                stopped = true;
                await Write(zip, "data/backup_label", label);
                if (map.Length > 0) await Write(zip, "data/tablespace_map", map);
                // A WAL do começo ao fim do backup (o wal_keep_size segura os segmentos); o segmento em uso entra pela metade, o que a recuperação aceita.
                foreach (var wal in Directory.EnumerateFiles(Path.Combine(Cluster, "pg_wal")))
                    await AddAsync(zip, wal, "data/pg_wal/" + Path.GetFileName(wal), ct);
                await AddAppFilesAsync(zip, AppPaths.Data, AppPaths.Config, ct);
            }
            File.Move(tmp, file, overwrite: true);
        }
        finally
        {
            if (!stopped)
            {
                try { await using var abort = new NpgsqlCommand("SELECT pg_backup_stop(false)", conn); await abort.ExecuteNonQueryAsync(CancellationToken.None); }
                catch (NpgsqlException) { }
            }
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        return file;

        static async Task Write(ZipArchive zip, string name, string text)
        {
            await using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Fastest).Open());
            await w.WriteAsync(text);
        }
    }

    /// <summary>The app files the database points to (resumes, generated PDFs, reports) and the settings, by their name in the backup.</summary>
    private static (string Name, string Path)[] AppFiles(string data, string config) =>
    [
        ("profile", Path.Combine(data, "profile")), ("resumes", Path.Combine(data, "resumes")), ("materials", Path.Combine(data, "materials")),
        ("reports", Path.Combine(data, "reports")), ("settings.json", Path.Combine(config, "settings.json")),
    ];

    /// <summary>
    /// The database alone points to files a restore would not bring back, so they go in the backup too, with a manifest of what is
    /// there. Passwords and keys stay in the system keychain and never enter a backup: the manifest says to connect them again.
    /// </summary>
    internal static async Task AddAppFilesAsync(ZipArchive zip, string data, string config, CancellationToken ct)
    {
        var included = new List<string>();
        foreach (var (name, path) in AppFiles(data, config))
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    await AddAsync(zip, file, $"files/{name}/{Path.GetRelativePath(path, file).Replace('\\', '/')}", ct);
                included.Add(name);
            }
            else if (File.Exists(path)) { await AddAsync(zip, path, $"files/{name}", ct); included.Add(name); }
        }
        await using var manifest = new StreamWriter(zip.CreateEntry("manifest.json", CompressionLevel.Fastest).Open());
        await manifest.WriteAsync(JsonSerializer.Serialize(new
        {
            version = 2, at = DateTime.UtcNow, database = "data/", files = included,
            notIncluded = "Senhas e chaves ficam no chaveiro do sistema e não entram no backup: depois de restaurar em outra máquina, conecte de novo as contas de e-mail e de IA."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Puts the app files of a backup back; what was there goes aside with the same stamp as the database. A backup without files leaves them as they are.</summary>
    internal static void RestoreAppFiles(string zipFile, string data, string config, string stamp)
    {
        using var zip = ZipFile.OpenRead(zipFile);
        foreach (var (name, path) in AppFiles(data, config))
        {
            var prefix = $"files/{name}";
            var entries = zip.Entries.Where(e => e.FullName == prefix || e.FullName.StartsWith(prefix + "/", StringComparison.Ordinal)).ToList();
            if (entries.Count == 0) continue;
            if (Directory.Exists(path)) Directory.Move(path, $"{path}.antes-{stamp}");
            else if (File.Exists(path)) File.Move(path, $"{path}.antes-{stamp}");
            var root = Path.GetFullPath(path);
            foreach (var entry in entries)
            {
                var dest = entry.FullName == prefix ? root : Path.GetFullPath(Path.Combine(root, entry.FullName[(prefix.Length + 1)..]));
                if (dest != root && !dest.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
        }
    }

    /// <summary>Arquivo que o Postgres pode estar gravando (ou apagando) agora: abre compartilhado e pula o que sumiu no meio.</summary>
    private static async Task AddAsync(ZipArchive zip, string path, string name, CancellationToken ct)
    {
        try
        {
            await using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await using var dst = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
            await src.CopyToAsync(dst, ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
    }

    /// <summary>Troca o cluster pelo de um backup (com o Postgres parado). O cluster de antes fica ao lado, como data.antes-&lt;data&gt;.</summary>
    public static string Restore(string zipFile)
    {
        if (File.Exists(Path.Combine(Cluster, "postmaster.pid"))) throw new InvalidOperationException("Pare o app antes (job-orchestrator --stop).");
        var stamp = $"{DateTime.Now:yyyyMMdd-HHmmss}";
        var aside = $"{Cluster}.antes-{stamp}";
        var staging = Cluster + ".restore";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        using (var zip = ZipFile.OpenRead(zipFile))
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("data/", StringComparison.Ordinal)))
            {
                var dest = Path.GetFullPath(Path.Combine(staging, entry.FullName[5..]));
                if (!dest.StartsWith(Path.GetFullPath(staging), StringComparison.Ordinal)) continue;
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
        if (!File.Exists(Path.Combine(staging, "PG_VERSION")) || !File.Exists(Path.Combine(staging, "backup_label"))) throw new InvalidOperationException("Esse arquivo não é um backup do banco do app.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (Directory.Exists(Cluster)) Directory.Move(Cluster, aside);
        Directory.Move(staging, Cluster);
        RestoreAppFiles(zipFile, AppPaths.Data, AppPaths.Config, stamp);
        return aside;
    }

    private static string Tail(string text) => text.Length > 400 ? text[^400..] : text.Trim();

    private static async Task<(int Exit, string Output)> RunAsync(string file, string[] args, TimeSpan limit, CancellationToken ct)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        // O initdb e o pg_ctl procuram as bibliotecas e o share ao lado do bin; LC_ALL fixo evita a mensagem traduzida pela metade.
        info.Environment["LC_ALL"] = "C";
        info.Environment.Remove("PGDATA");
        using var p = Process.Start(info) ?? throw new InvalidOperationException($"{file} não iniciou.");
        // pg_ctl start deixa o postmaster com a saída no log (-l); a leitura dos pipes termina quando o pg_ctl sai.
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } return (-1, "tempo esgotado"); }
        return (p.ExitCode, await stdout + await stderr);
    }
}
