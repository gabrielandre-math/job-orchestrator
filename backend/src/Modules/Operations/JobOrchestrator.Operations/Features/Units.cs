using System.Diagnostics;
using JobOrchestrator.BuildingBlocks.Data;

namespace JobOrchestrator.Operations.Features;

internal sealed record TimerView(string Name, string Label, bool Enabled, bool Active, DateTime? Next);
internal sealed record UnitsView(bool Available, string? Reason, IReadOnlyList<TimerView> Timers, string Mode = "systemd");
internal sealed record NightInput(bool On);

/// <summary>
/// Os timers da noite pelo painel, como serviços do seu usuário no systemd: rodadas às 23h05, 1h35 e 4h05, resumo às 7h45 e backup às 7h55.
/// Liga só os timers: quem atende as chamadas deles é o painel em 127.0.0.1:7410 (dev, Docker ou os serviços de scripts/install-units.sh).
/// Só liga quando você aperta o botão; desligar devolve tudo como estava.
/// </summary>
internal static class Units
{
    private static readonly (string Name, string Label)[] Timers =
        [("job-orchestrator-night", "Rodadas da noite: 23h05, 1h35 e 4h05"), ("job-orchestrator-report", "Resumo da manhã: 7h45"), ("job-orchestrator-backup", "Backup do banco: 7h55")];
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string UserDir => Path.Combine(Home, ".config", "systemd", "user");

    /// <summary>O systemd do usuário existe aqui (Linux com sessão). Sem ele (macOS, Windows, contêiner), o próprio app faz as rodadas (NightClock).</summary>
    public static bool Systemd => OperatingSystem.IsLinux() && File.Exists("/usr/bin/systemctl") && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 };

    public static async Task<UnitsView> StatusAsync(IConfiguration config, CancellationToken ct, bool? on = null)
    {
        if (!Systemd)
        {
            var enabled = on ?? config.GetValue("Night:InApp", false);
            return new(true, null, [.. Timers.Select(t => new TimerView(t.Name, t.Label, enabled, enabled, enabled ? NightClock.Next(t.Name, DateTime.Now) : null))], "app");
        }
        var list = new List<TimerView>();
        foreach (var (name, label) in Timers)
        {
            var (_, output) = await RunAsync(["--user", "show", $"{name}.timer", "-p", "UnitFileState", "-p", "ActiveState", "-p", "NextElapseUSecRealtime", "--timestamp=unix"], ct);
            var props = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1].Trim());
            DateTime? next = props.GetValueOrDefault("NextElapseUSecRealtime") is { Length: > 1 } v && v[0] == '@' && long.TryParse(v[1..], out var s) ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime : null;
            list.Add(new(name, label, props.GetValueOrDefault("UnitFileState") == "enabled", props.GetValueOrDefault("ActiveState") == "active", next));
        }
        return new(true, null, list);
    }

    public static async Task<UnitsView> SetAsync(IConfiguration config, bool on, CancellationToken ct)
    {
        if (!Systemd)
        {
            await UserSettings.SetAsync(new Dictionary<string, object?> { ["Night:InApp"] = on });
            return await StatusAsync(config, ct, on);
        }
        var status = await StatusAsync(config, ct);
        if (!status.Available) return status;
        string[] timers = [.. Timers.Select(t => $"{t.Name}.timer")];
        if (on)
        {
            Install();
            await RunAsync(["--user", "daemon-reload"], ct);
        }
        var (exit, output) = await RunAsync(["--user", on ? "enable" : "disable", "--now", .. timers], ct);
        if (exit != 0) throw new InvalidOperationException(output.Trim());
        return await StatusAsync(config, ct);
    }

    /// <summary>Copia as units que faltam (timers, os serviços deles e a fatia jobs.slice) e o script de backup para a pasta instalada.</summary>
    private static void Install()
    {
        var repo = Repo() ?? throw new InvalidOperationException("Não achei a pasta infra/systemd do projeto para instalar os timers.");
        Directory.CreateDirectory(UserDir);
        foreach (var file in Timers.SelectMany(t => new[] { $"{t.Name}.timer", $"{t.Name}.service" }).Append("jobs.slice"))
        {
            var target = Path.Combine(UserDir, file);
            if (!File.Exists(target)) File.Copy(Path.Combine(repo, "infra", "systemd", file), target);
        }
        var opt = Path.Combine(Home, ".local", "opt", "job-orchestrator");
        var backup = Path.Combine(opt, "backup.sh");
        if (!File.Exists(backup))
        {
            Directory.CreateDirectory(opt);
            File.Copy(Path.Combine(repo, "scripts", "backup.sh"), backup);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(backup, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static string? Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "infra", "systemd")) && File.Exists(Path.Combine(dir.FullName, "scripts", "backup.sh"))) return dir.FullName;
        return null;
    }

    private static async Task<(int Exit, string Output)> RunAsync(string[] args, CancellationToken ct)
    {
        var info = new ProcessStartInfo("/usr/bin/systemctl") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        var error = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, output + error);
    }
}

internal sealed class UnitsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/units").WithTags("Operations");
        g.MapGet("", async (IConfiguration config, CancellationToken ct) => Results.Ok(await Units.StatusAsync(config, ct)));
        g.MapPut("night", async (NightInput body, IConfiguration config, CancellationToken ct) =>
        {
            try { return Results.Ok(await Units.SetAsync(config, body.On, ct)); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return Results.Problem(title: "Não foi possível mudar os timers", detail: ex.Message, statusCode: 409);
            }
        });
    }
}

/// <summary>
/// As rodadas da noite sem o systemd (macOS, Windows ou Linux sem sessão de usuário): o próprio app dispara as rodadas às 23h05, 1h35 e
/// 4h05, o resumo às 7h45 e o backup às 7h55, no horário da máquina, enquanto estiver no ar. Liga e desliga por Night:InApp. Máquina que
/// dormiu no horário faz, ao acordar, uma rodada só, e não uma por horário perdido.
/// </summary>
internal sealed class NightClock(IServiceProvider services, IConfiguration config, NightReports reports, ILogger<NightClock> log) : BackgroundService
{
    private static readonly (string Name, TimeOnly At)[] Schedule =
    [
        ("job-orchestrator-night", new(23, 5)), ("job-orchestrator-night", new(1, 35)), ("job-orchestrator-night", new(4, 5)),
        ("job-orchestrator-report", new(7, 45)), ("job-orchestrator-backup", new(7, 55)),
    ];

    /// <summary>O próximo horário de um timer, a partir de agora.</summary>
    public static DateTime Next(string name, DateTime now) =>
        Schedule.Where(s => s.Name == name).Select(s => now.Date + s.At.ToTimeSpan()).Select(d => d <= now ? d.AddDays(1) : d).Min().ToUniversalTime();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Units.Systemd) return;
        var last = DateTime.Now;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
            var now = DateTime.Now;
            if (config.GetValue("Night:InApp", false))
                foreach (var job in Schedule.Where(s => Due(s.At, last, now)).Select(s => s.Name).Distinct().ToList())
                {
                    try { await RunAsync(job, stoppingToken); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Night job {Job} failed", job); }
                }
            last = now;
        }
    }

    /// <summary>O horário caiu entre a última conferência e agora (de hoje ou de ontem, para a virada da meia-noite).</summary>
    internal static bool Due(TimeOnly at, DateTime last, DateTime now) =>
        new[] { now.Date + at.ToTimeSpan(), now.Date.AddDays(-1) + at.ToTimeSpan() }.Any(d => d > last && d <= now);

    private async Task RunAsync(string job, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        switch (job)
        {
            case "job-orchestrator-night":
                var asked = await scope.ServiceProvider.GetRequiredService<JobOrchestrator.Campaigns.Contracts.ICampaignRuns>().RequestEnabledAsync("night", ct);
                log.LogInformation("Night round: {Count} campaigns", asked);
                break;
            case "job-orchestrator-report":
                await reports.WriteAsync(null);
                break;
            case "job-orchestrator-backup":
                log.LogInformation("Backup: {Result}", await Backup.RunAsync(config, ct));
                break;
        }
    }
}

/// <summary>
/// O backup do banco às 7h55: com o Postgres próprio, a cópia física com o banco no ar (PortablePostgres.BackupAsync); com o do Docker,
/// o pg_dump de dentro do contêiner, comprimido. Ficam os 14 mais novos em &lt;dados&gt;/backups.
/// </summary>
internal static class Backup
{
    public static async Task<string> RunAsync(IConfiguration config, CancellationToken ct)
    {
        var dir = AppPaths.In(AppPaths.Data, "backups");
        string file;
        if (PortablePostgres.Installed)
            file = await PortablePostgres.BackupAsync(config.GetConnectionString("Default") ?? "", dir, ct);
        else if (Platform.Which("docker") is { } docker)
        {
            file = Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd-HHmm}.sql.gz");
            var info = new ProcessStartInfo(docker, ["exec", "job-orchestrator-postgres-1", "pg_dump", "-U", "jo", "-d", "job_orchestrator", "--no-owner"])
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(info) ?? throw new InvalidOperationException("docker não iniciou.");
            var errors = p.StandardError.ReadToEndAsync(ct);
            await using (var output = File.Create(file + ".tmp"))
            await using (var gz = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.SmallestSize))
                await p.StandardOutput.BaseStream.CopyToAsync(gz, ct);
            await p.WaitForExitAsync(ct);
            if (p.ExitCode != 0) { File.Delete(file + ".tmp"); return $"pg_dump falhou: {(await errors).Trim()}"; }
            File.Move(file + ".tmp", file, overwrite: true);
        }
        else return "sem banco para copiar (nem Postgres próprio nem Docker)";
        foreach (var old in Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".pgdata.zip", StringComparison.Ordinal) || f.EndsWith(".sql.gz", StringComparison.Ordinal))
            .OrderByDescending(f => f, StringComparer.Ordinal).Skip(14))
            File.Delete(old);
        return $"{Path.GetFileName(file)} ({new FileInfo(file).Length / 1_000_000.0:0.0} MB)";
    }
}
