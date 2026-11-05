// jo: linha de comando do orquestrador: o bench dos modelos locais e a sondagem do registro de fontes.
var command = string.Join(' ', args.TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)));
string? Option(string name) => args.SkipWhile(a => a != $"--{name}").Skip(1).FirstOrDefault();

switch (command)
{
    case "llm bench":
        var models = (Option("models") ?? "qwen3:4b,qwen3.5:4b,qwen3.5:9b,gemma4:e4b").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return await Bench.RunAsync(models, Option("url") ?? "http://127.0.0.1:11434", Option("out"), Option("json"));
    case "sources probe":
        return await Sources.ProbeAsync(Option("api") ?? "http://127.0.0.1:7410", Option("json") ?? "data/source-registry.json", Option("md") ?? "docs/SOURCE_AUDIT.md");
    case "sources coverage":
        return await CoverageReport.WriteAsync(Option("api") ?? "http://127.0.0.1:7410", Option("md") ?? "docs/COVERAGE_GAPS.md");
    default:
        Console.WriteLine("""
            uso:
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
