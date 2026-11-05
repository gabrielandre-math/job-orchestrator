using System.Security.Cryptography;

namespace JobOrchestrator.BuildingBlocks;

/// <summary>Chave do Guardião para assinar autorizações de envio. Fica em ~/.config/job-orchestrator/guard.key, só com leitura do dono.</summary>
public sealed class GuardKey
{
    public GuardKey() : this(Path.Combine(AppPaths.Config, "guard.key")) { }

    public GuardKey(string path)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        Bytes = Convert.FromBase64String(File.ReadAllText(path).Trim());
    }

    public byte[] Bytes { get; }
}
