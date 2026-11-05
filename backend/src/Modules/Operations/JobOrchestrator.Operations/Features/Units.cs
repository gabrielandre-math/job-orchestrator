using System.Diagnostics;

namespace JobOrchestrator.Operations.Features;

internal sealed record TimerView(string Name, string Label, bool Enabled, bool Active, DateTime? Next);
internal sealed record UnitsView(bool Available, string? Reason, IReadOnlyList<TimerView> Timers);
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

    public static async Task<UnitsView> StatusAsync(CancellationToken ct)
    {
        if (!File.Exists("/usr/bin/systemctl") || Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is not { Length: > 0 })
            return new(false, "Aqui não tem o systemd do seu usuário (Docker ou outro sistema). Use os timers do host ou scripts/install-units.sh.", []);
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

    public static async Task<UnitsView> SetAsync(bool on, CancellationToken ct)
    {
        var status = await StatusAsync(ct);
        if (!status.Available) return status;
        string[] timers = [.. Timers.Select(t => $"{t.Name}.timer")];
        if (on)
        {
            Install();
            await RunAsync(["--user", "daemon-reload"], ct);
        }
        var (exit, output) = await RunAsync(["--user", on ? "enable" : "disable", "--now", .. timers], ct);
        if (exit != 0) throw new InvalidOperationException(output.Trim());
        return await StatusAsync(ct);
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
        g.MapGet("", async (CancellationToken ct) => Results.Ok(await Units.StatusAsync(ct)));
        g.MapPut("night", async (NightInput body, CancellationToken ct) =>
        {
            try { return Results.Ok(await Units.SetAsync(body.On, ct)); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return Results.Problem(title: "Não foi possível mudar os timers", detail: ex.Message, statusCode: 409);
            }
        });
    }
}
