using System.Globalization;
using JobOrchestrator.Intelligence.Providers;

namespace JobOrchestrator.Intelligence.Features;

internal sealed record GpuSensor(string Vendor, string Name, double? TempC, double? UtilPct, int? VramUsedMb, int? VramTotalMb, double? PowerW, double? PowerLimitW,
    int? ClockMhz, int? ClockMaxMhz, int? MemClockMhz, int? MemClockMaxMhz, double? FanPct);
internal sealed record CpuSensor(string Name, double? TempC, double? HottestCoreC, double? UtilPct, int? ClockMhz, int? ClockMaxMhz, int Threads);
internal sealed record FanSensor(string Label, int Rpm);
internal sealed record SensorSample(DateTime At, IReadOnlyList<GpuSensor> Gpus, CpuSensor Cpu, long RamUsedMb, long RamTotalMb, IReadOnlyList<FanSensor> Fans, double? NvmeC, double? RamC);

/// <summary>
/// Sensores da máquina para a tela de provedores: GPU pelo nvidia-smi ou pelo sysfs do amdgpu, CPU pelo hwmon (coretemp, k10temp, zenpower)
/// e pelo /proc/stat, ventoinhas, NVMe e RAM. Só leitura; uma amostra vale 800 ms para vários painéis abertos não multiplicarem o custo.
/// </summary>
internal sealed class Sensors
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private (long Idle, long Total)? lastCpu;
    private SensorSample? cached;
    private DateTime cachedAt;

    public async Task<SensorSample> SampleAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (cached is not null && DateTime.UtcNow - cachedAt < TimeSpan.FromMilliseconds(800)) return cached;
            cached = Memory(new SensorSample(DateTime.UtcNow, await GpusAsync(ct), Cpu(), 0, 0, Fans(), Hwmon("nvme").Select(h => Temp(h, 1)).FirstOrDefault(t => t is not null), RamTemp()));
            cachedAt = DateTime.UtcNow;
            return cached;
        }
        finally { gate.Release(); }
    }

    private static async Task<IReadOnlyList<GpuSensor>> GpusAsync(CancellationToken ct)
    {
        var list = new List<GpuSensor>();
        if (CliRunner.Find("nvidia-smi") is { } smi)
        {
            var run = await CliRunner.RunAsync(smi, ["--query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total,power.draw,power.limit,clocks.gr,clocks.max.gr,clocks.mem,clocks.max.mem,fan.speed", "--format=csv,noheader,nounits"],
                null, TimeSpan.FromSeconds(5), null, ct);
            foreach (var line in run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var c = line.Split(',', StringSplitOptions.TrimEntries);
                if (c.Length < 12) continue;
                list.Add(new GpuSensor("nvidia", c[0], D(c[1]), D(c[2]), I(c[3]), I(c[4]), D(c[5]), D(c[6]), I(c[7]), I(c[8]), I(c[9]), I(c[10]), D(c[11])));
            }
        }
        // AMD: tudo pelo sysfs do amdgpu.
        foreach (var dev in Directory.Exists("/sys/class/drm") ? Directory.GetDirectories("/sys/class/drm", "card?") : [])
        {
            var d = Path.Combine(dev, "device");
            if (!File.Exists(Path.Combine(d, "gpu_busy_percent"))) continue;
            var hw = Directory.Exists(Path.Combine(d, "hwmon")) ? Directory.GetDirectories(Path.Combine(d, "hwmon")).FirstOrDefault() : null;
            var (sclk, sclkMax) = Dpm(Path.Combine(d, "pp_dpm_sclk"));
            var (mclk, mclkMax) = Dpm(Path.Combine(d, "pp_dpm_mclk"));
            list.Add(new GpuSensor("amd", "AMD Radeon", hw is null ? null : Temp(hw, 1), D(Read(Path.Combine(d, "gpu_busy_percent"))),
                Mb(Read(Path.Combine(d, "mem_info_vram_used"))), Mb(Read(Path.Combine(d, "mem_info_vram_total"))),
                hw is null ? null : Micro(Read(Path.Combine(hw, "power1_average")) ?? Read(Path.Combine(hw, "power1_input"))), hw is null ? null : Micro(Read(Path.Combine(hw, "power1_cap"))),
                sclk, sclkMax, mclk, mclkMax, null));
        }
        return list;
    }

    private CpuSensor Cpu()
    {
        var info = File.Exists("/proc/cpuinfo") ? File.ReadAllLines("/proc/cpuinfo") : [];
        var name = info.FirstOrDefault(l => l.StartsWith("model name"))?.Split(':', 2)[1].Trim() ?? "CPU";
        var threads = info.Count(l => l.StartsWith("processor"));
        double? package = null, hottest = null;
        foreach (var hw in Hwmon("coretemp", "k10temp", "zenpower"))
        {
            foreach (var label in Directory.GetFiles(hw, "temp*_label"))
            {
                var text = Read(label) ?? "";
                var value = Milli(Read(label.Replace("_label", "_input")));
                if (value is null) continue;
                if (text.StartsWith("Package") || text is "Tctl" or "Tdie") package ??= value;
                if (text.StartsWith("Core") || text.StartsWith("Tccd")) hottest = Math.Max(hottest ?? 0, value.Value);
            }
            package ??= Temp(hw, 1);
        }
        package ??= Hwmon("acpitz").Select(h => Temp(h, 1)).FirstOrDefault(t => t is not null);

        double? util = null;
        var stat = File.Exists("/proc/stat") ? File.ReadLines("/proc/stat").FirstOrDefault() : null;
        if (stat is not null)
        {
            var n = stat.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(long.Parse).ToArray();
            var idle = n[3] + n[4];
            var total = n.Sum();
            if (lastCpu is { } last && total > last.Total) util = Math.Round(100.0 * (1 - (double)(idle - last.Idle) / (total - last.Total)), 1);
            lastCpu = (idle, total);
        }

        var freqs = Directory.Exists("/sys/devices/system/cpu") ? Directory.GetDirectories("/sys/devices/system/cpu", "cpu*").Select(c => Path.Combine(c, "cpufreq")).Where(Directory.Exists).ToList() : [];
        var cur = freqs.Select(f => D(Read(Path.Combine(f, "scaling_cur_freq")))).Where(x => x is not null).Select(x => x!.Value / 1000).ToList();
        var max = freqs.Select(f => D(Read(Path.Combine(f, "cpuinfo_max_freq")))).Where(x => x is not null).Select(x => x!.Value / 1000).DefaultIfEmpty(0).Max();
        return new CpuSensor(name, package, hottest, util, cur.Count > 0 ? (int)cur.Average() : null, max > 0 ? (int)max : null, threads);
    }

    private static SensorSample Memory(SensorSample s)
    {
        var mem = File.Exists("/proc/meminfo") ? File.ReadAllLines("/proc/meminfo") : [];
        long Kb(string key) => long.TryParse(mem.FirstOrDefault(l => l.StartsWith(key))?.Split(':')[1].Trim().Split(' ')[0], out var v) ? v : 0;
        var total = Kb("MemTotal") / 1024;
        return s with { RamTotalMb = total, RamUsedMb = total - Kb("MemAvailable") / 1024 };
    }

    private static IReadOnlyList<FanSensor> Fans()
    {
        var fans = new List<FanSensor>();
        foreach (var hw in Hwmon())
            foreach (var input in Directory.GetFiles(hw, "fan*_input").Order())
            {
                if (!int.TryParse(Read(input), out var rpm) || rpm <= 0) continue;
                var label = Read(input.Replace("_input", "_label")) ?? $"Ventoinha {Path.GetFileName(input)[3..^6]}";
                fans.Add(new FanSensor(label, rpm));
            }
        return fans;
    }

    private static double? RamTemp() => Hwmon("spd5118", "jc42").Select(h => Temp(h, 1)).Where(t => t is not null).DefaultIfEmpty(null).Max();

    private static IEnumerable<string> Hwmon(params string[] names) =>
        (Directory.Exists("/sys/class/hwmon") ? Directory.GetDirectories("/sys/class/hwmon") : []).Where(h => names.Length == 0 || names.Contains(Read(Path.Combine(h, "name"))));

    private static double? Temp(string hw, int n) => Milli(Read(Path.Combine(hw, $"temp{n}_input")));
    private static (int?, int?) Dpm(string path)
    {
        var lines = Read(path)?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [];
        int? Mhz(string l) => int.TryParse(new string(l.Split(':').Last().TakeWhile(char.IsDigit).ToArray()).Trim(), out var v) ? v : null;
        return (lines.FirstOrDefault(l => l.Contains('*')) is { } on ? Mhz(on.Split(':', 2)[1].Trim()) : null, lines.Length > 0 ? Mhz(lines[^1].Split(':', 2)[1].Trim()) : null);
    }

    private static string? Read(string path) { try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; } catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; } }
    private static double? D(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    private static int? I(string? s) => D(s) is { } v ? (int)Math.Round(v) : null;
    private static double? Milli(string? s) => D(s) is { } v ? Math.Round(v / 1000, 1) : null;
    private static double? Micro(string? s) => D(s) is { } v ? Math.Round(v / 1_000_000, 1) : null;
    private static int? Mb(string? s) => D(s) is { } v ? (int)(v / 1024 / 1024) : null;
}

/// <summary>Rotas dos sensores: amostra única e stream SSE de uma por segundo enquanto a tela estiver aberta.</summary>
internal sealed class SensorEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/local/sensors").WithTags("Intelligence");
        g.MapGet("", async (Sensors sensors, CancellationToken ct) => Results.Ok(await sensors.SampleAsync(ct)));
        g.MapGet("stream", async (HttpContext http, Sensors sensors, CancellationToken ct) =>
        {
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            try
            {
                await http.Response.WriteAsync(": conectado\n\n", ct);
                await http.Response.Body.FlushAsync(ct);
                while (!ct.IsCancellationRequested)
                {
                    var s = await sensors.SampleAsync(ct);
                    await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(s, JsonSerializerOptions.Web)}\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                    await Task.Delay(1000, ct);
                }
            }
            catch (OperationCanceledException) { /* a tela fechou */ }
        });
    }
}
