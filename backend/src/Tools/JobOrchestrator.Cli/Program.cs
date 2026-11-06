// jo: linha de comando do orquestrador: o bench dos modelos locais, a sondagem do registro de fontes e o import histórico.
var command = string.Join(' ', args.TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)));
string? Option(string name) => args.SkipWhile(a => a != $"--{name}").Skip(1).FirstOrDefault();

switch (command)
{
    case "llm bench":
        var models = (Option("models") ?? "qwen3:4b,qwen3.5:4b,qwen3.5:9b,gemma4:e4b").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return await Bench.RunAsync(models, Option("url") ?? "http://127.0.0.1:11434", Option("out"), Option("json"));
    case "sources probe":
        return await Sources.ProbeAsync(Option("api") ?? "http://127.0.0.1:7410", Option("json") ?? "data/source-registry.json", Option("md") ?? "docs/SOURCE_AUDIT.md");
    case "sources boards":
        return await Boards.ImportAsync(Option("api") ?? "http://127.0.0.1:7410", Option("out") ?? "data/ats-board-catalog.json", int.TryParse(Option("sample"), out var n) ? n : 60);
    case "sources next":
        return await NextSources.WriteAsync(Option("catalog") ?? "data/catalog/catalogo-fontes-v2-2026.json", Option("registry") ?? "data/source-registry.json", Option("md") ?? "docs/NEXT_SOURCES.md");
    case "sources coverage":
        return await CoverageReport.WriteAsync(Option("api") ?? "http://127.0.0.1:7410", Option("md") ?? "docs/COVERAGE_GAPS.md");
    case var history when history == "history import" || history.StartsWith("history import ", StringComparison.Ordinal):
        var from = history.Length > "history import ".Length ? history["history import ".Length..] : Option("from");
        if (from is null) { Console.Error.WriteLine("uso: jo history import <arquivo, pasta ou URL do .tar> [opções]"); return 2; }
        return await HistoryImport.ImportAsync(Option("api") ?? "http://127.0.0.1:7410", from, Option("snapshot") ?? "openjobs:2026-10-05", args.Contains("--all"),
            int.TryParse(Option("max-rows"), out var maxRows) ? maxRows : 100_000, (long.TryParse(Option("disk-mb"), out var diskMb) ? diskMb : 8192) << 20,
            Option("work") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "job-orchestrator", "history"),
            int.TryParse(Option("batch"), out var batch) ? Math.Clamp(batch, 1, 5000) : 1000);
    case "history revalidate":
        return await HistoryImport.RevalidateAsync(Option("api") ?? "http://127.0.0.1:7410", int.TryParse(Option("boards"), out var boards) ? boards : 10);
    case "history status":
        return await HistoryImport.StatusAsync(Option("api") ?? "http://127.0.0.1:7410");
    default:
        Console.WriteLine("""
            uso:
              jo history import <arquivo.parquet | arquivo.csv[.gz] | pasta | export.tar | URL do .tar> [--snapshot openjobs:2026-10-05] [--all]
                  [--max-rows 100000] [--disk-mb 8192] [--work ~/.cache/job-orchestrator/history] [--batch 1000] [--api http://127.0.0.1:7410]
                import histórico em streaming (Open Jobs ou export no mesmo formato): só as palavras da campanha, salvo --all; tudo entra
                como não conferido e nunca aberto. O .tar vai membro a membro dentro do orçamento de disco, e o que já entrou é pulado
              jo history revalidate [--boards 10]    confere com a fonte ao vivo: abre só o que o quadro lista hoje
              jo history status                      o resumo do histórico importado
              jo sources boards [--api http://127.0.0.1:7410] [--out data/ats-board-catalog.json] [--sample 60]
                importa os catálogos públicos de quadros de ATS, deduplica, guarda a licença e valida uma amostra de verdade
              jo sources next [--catalog ...] [--registry data/source-registry.json] [--md docs/NEXT_SOURCES.md]
                o inventário priorizado do que falta no catálogo V2, com o caminho para cada tipo de acesso
              jo sources coverage [--api http://127.0.0.1:7410] [--md docs/COVERAGE_GAPS.md]
                a cobertura dos 249 países, por continente e país, com o que existe e o que falta
              jo sources probe [--api http://127.0.0.1:7410] [--json data/source-registry.json] [--md docs/SOURCE_AUDIT.md]
                sonda de verdade cada plataforma do registro pela API do app e escreve o registro tipado e a auditoria
              jo llm bench [--models a,b,c] [--out arquivo.md] [--json resultado.json] [--url http://127.0.0.1:11434]
                mede os modelos locais nas tarefas reais (job.extract, email.classify, question.map):
                JSON válido, acerto nos 50 casos, latência, carga fria e VRAM
            """);
        return command.Length == 0 ? 0 : 2;
}
