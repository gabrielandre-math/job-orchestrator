using JobOrchestrator.App;

// O executável do Job Orchestrator, para uso local.
// Sem argumento: abre o app (sobe os serviços em segundo plano se preciso, mostra a abertura e a janela).
// --serve: API, painel e Worker num processo só, em 127.0.0.1:7410. --setup: o instalador. --stop e --status: parar e conferir.
return args.FirstOrDefault() switch
{
    "--serve" => await Server.RunAsync(args[1..]),
    "--setup" => await Setup.RunAsync(),
    "--stop" => await Launcher.StopAsync(),
    "--status" => await Launcher.StatusAsync(),
    "--version" => Launcher.Print($"Job Orchestrator {Launcher.Version}"),
    "--help" or "-h" => Launcher.Print("""
        job-orchestrator            abre o app (sobe os serviços se preciso)
        job-orchestrator --serve    roda os serviços neste terminal
        job-orchestrator --setup    abre o instalador
        job-orchestrator --status   mostra se está no ar
        job-orchestrator --stop     para os serviços
        """),
    _ => await Launcher.OpenAsync(),
};
