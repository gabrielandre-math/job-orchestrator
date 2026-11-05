// jo: linha de comando do orquestrador. Por enquanto, o bench dos modelos locais.
var command = string.Join(' ', args.TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)));
string? Option(string name) => args.SkipWhile(a => a != $"--{name}").Skip(1).FirstOrDefault();

switch (command)
{
    case "llm bench":
        var models = (Option("models") ?? "qwen3:4b,qwen3.5:4b,qwen3.5:9b,gemma4:e4b").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return await Bench.RunAsync(models, Option("url") ?? "http://127.0.0.1:11434", Option("out"), Option("json"));
    default:
        Console.WriteLine("""
            uso:
              jo llm bench [--models a,b,c] [--out arquivo.md] [--json resultado.json] [--url http://127.0.0.1:11434]
                mede os modelos locais nas tarefas reais (job.extract, email.classify, question.map):
                JSON válido, acerto nos 50 casos, latência, carga fria e VRAM
            """);
        return command.Length == 0 ? 0 : 2;
}
