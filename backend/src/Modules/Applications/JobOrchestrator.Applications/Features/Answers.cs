using System.Text.Json;

namespace JobOrchestrator.Applications.Features;

internal sealed record SavedAnswer(string Question, string Answer, DateTime At);
internal sealed record AnswerInput(string Question, string? Answer);

/// <summary>
/// As suas respostas às perguntas que o perfil não responde ("tem 1 ano como líder?"). Ficam num arquivo da pasta de dados, por
/// texto da pergunta sem acento e pontuação, e todo formulário que fizer a mesma pergunta usa a mesma resposta. Resposta vazia apaga.
/// </summary>
internal static class AnswerStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Lock Gate = new();
    private static string FilePath => Path.Combine(AppPaths.Data, "answers.json");

    public static Dictionary<string, SavedAnswer> Load()
    {
        lock (Gate)
        {
            try { return File.Exists(FilePath) ? (JsonSerializer.Deserialize<Dictionary<string, SavedAnswer>>(File.ReadAllText(FilePath), Json) ?? []).Values.OrderByDescending(a => a.At).DistinctBy(a => CustomAnswer.Key(a.Question)).ToDictionary(a => CustomAnswer.Key(a.Question)) : []; }
            catch (Exception ex) when (ex is JsonException or IOException) { return []; }
        }
    }

    public static void Save(string question, string? answer, DateTime now)
    {
        SaveMany([new(question, answer)], now);
    }

    public static void SaveMany(IReadOnlyList<AnswerInput> answers, DateTime now)
    {
        lock (Gate)
        {
            var all = Load();
            foreach (var a in answers)
            {
                var key = CustomAnswer.Key(a.Question);
                if (string.IsNullOrWhiteSpace(a.Answer)) all.Remove(key); else all[key] = new SavedAnswer(a.Question.Trim(), a.Answer.Trim(), now);
            }
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(all, Json));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(tmp, FilePath, overwrite: true);
        }
    }
}

internal sealed class AnswerEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/applications/answers").WithTags("Applications");
        g.MapGet("", () => Results.Ok(AnswerStore.Load().Values.OrderByDescending(a => a.At).Select(a => new { a.Question, a.Answer, a.At, Key = CustomAnswer.Key(a.Question) })));
        g.MapPut("", async (AnswerInput body, ISender sender, CancellationToken ct) =>
            (await sender.Send(new SaveFormAnswersCommand([body]), ct)).ToHttp(r => Results.Ok(r)));
    }
}
