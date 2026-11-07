using System.Runtime.InteropServices;
using JobOrchestrator.App;

// O executável do Job Orchestrator, para uso local, no Linux, no macOS e no Windows.
// Sem argumento: abre o app (sobe os serviços em segundo plano se preciso, mostra a abertura e a janela); na primeira vez, o instalador.
// --serve: API, painel e Worker num processo só, em 127.0.0.1:7410. --setup: o instalador. --stop e --status: parar e conferir.
if (OperatingSystem.IsWindows()) WindowsConsole.Attach();

// O Setup.exe do Windows é o próprio app com o pacote colado no fim: com dois cliques, extrai e abre o instalador.
if (args.Length == 0 && OperatingSystem.IsWindows() && Environment.ProcessPath is { } self && Payload.Present(self)) return Payload.Run(self);

return args.FirstOrDefault() switch
{
    "--serve" => await Server.RunAsync(args[1..]),
    "--mcp" => await Mcp.RunAsync(args[1..]),
    "--setup" => await Setup.RunAsync(),
    "--stop" => await Launcher.StopAsync(),
    "--status" => await Launcher.StatusAsync(),
    "--diagnose" => await Launcher.DiagnoseAsync(),
    "--uninstall" => await Maintenance.UninstallAsync(args.Contains("--purge")),
    "--restore" when args.Length > 1 => await Maintenance.RestoreAsync(args[1]),
    "--backup" => await Maintenance.BackupAsync(),
    "--selftest" => await Maintenance.SelfTestAsync(),
    "--version" => Launcher.Print($"Job Orchestrator {Launcher.Version} ({RuntimeInformation.RuntimeIdentifier})"),
    "--help" or "-h" => Launcher.Print("""
        job-orchestrator                 abre o app (sobe os serviços se preciso; na primeira vez, o instalador)
        job-orchestrator --serve         roda os serviços neste terminal
        job-orchestrator --setup         abre o instalador
        job-orchestrator --status        mostra se está no ar
        job-orchestrator --diagnose      conta os bloqueios da fila local, sem currículo ou credenciais
        job-orchestrator --mcp           conector MCP (Claude Code, Claude Desktop): claude mcp add job-orchestrator -- job-orchestrator --mcp
        job-orchestrator --stop          para os serviços
        job-orchestrator --backup        copia o banco agora (com o app no ar)
        job-orchestrator --restore ZIP   restaura o banco de um backup
        job-orchestrator --uninstall     tira o app (os seus dados ficam; com --purge, saem também)
        """),
    // Nada no ar, nada instalado e rodando de fora da pasta de instalação: é a primeira vez, então abre o instalador.
    _ when !await Launcher.HealthyAsync() && !Launcher.Installed && !Launcher.FromTarget => await Setup.RunAsync(),
    _ => await Launcher.OpenAsync(),
};

/// <summary>No Windows o app é de janela (dois cliques não abrem console); pelo terminal, a saída vai para o console de quem chamou.</summary>
internal static class WindowsConsole
{
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    public static void Attach()
    {
        // Saída em UTF-8: os acentos do português chegam certos no Terminal do Windows e em arquivo redirecionado.
        if (AttachConsole(-1)) Console.OutputEncoding = System.Text.Encoding.UTF8;
    }
}
