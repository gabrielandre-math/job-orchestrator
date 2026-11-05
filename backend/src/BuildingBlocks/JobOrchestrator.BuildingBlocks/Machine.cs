using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace JobOrchestrator.BuildingBlocks;

/// <summary>Processador e memória, lidos do jeito de cada sistema: /proc no Linux, sysctl e vm_stat no macOS, registro e kernel32 no Windows.</summary>
public static partial class Machine
{
    public static (string Cpu, int RamTotalMb, int RamFreeMb) Describe()
    {
        if (OperatingSystem.IsMacOS())
        {
            var cpu = Platform.Capture("/usr/sbin/sysctl", "-n", "machdep.cpu.brand_string").Trim();
            _ = long.TryParse(Platform.Capture("/usr/sbin/sysctl", "-n", "hw.memsize").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var total);
            // Livre de verdade no macOS: páginas livres, inativas e especulativas, que o sistema entrega na hora.
            var vm = Platform.Capture("/usr/bin/vm_stat");
            var page = PageSize().Match(vm) is { Success: true } m ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 16384;
            long Pages(string key) => Regex.Match(vm, $@"{key}:\s+(\d+)") is { Success: true } p ? long.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            var free = (Pages("Pages free") + Pages("Pages inactive") + Pages("Pages speculative")) * page;
            return (cpu.Length > 0 ? cpu : "CPU", (int)(total / 1048576), (int)(free / 1048576));
        }
        if (OperatingSystem.IsWindows())
        {
            var cpu = (Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")?.GetValue("ProcessorNameString") as string)?.Trim();
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            return GlobalMemoryStatusEx(ref status)
                ? (cpu is { Length: > 0 } ? cpu : "CPU", (int)(status.TotalPhys / 1048576), (int)(status.AvailPhys / 1048576))
                : (cpu ?? "CPU", 0, 0);
        }
        var cpuinfo = File.Exists("/proc/cpuinfo") ? File.ReadAllText("/proc/cpuinfo") : "";
        var model = cpuinfo.Split('\n').FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal))?.Split(':', 2)[1].Trim() ?? "CPU";
        var mem = File.Exists("/proc/meminfo") ? File.ReadAllLines("/proc/meminfo") : [];
        int Mem(string key) => mem.FirstOrDefault(l => l.StartsWith(key + ":", StringComparison.Ordinal)) is { } l
            && int.TryParse(l.Split(':')[1].Replace("kB", "", StringComparison.Ordinal).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb) ? kb / 1024 : 0;
        return (model, Mem("MemTotal"), Mem("MemAvailable"));
    }

    [GeneratedRegex(@"page size of (\d+) bytes")]
    private static partial Regex PageSize();

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}
