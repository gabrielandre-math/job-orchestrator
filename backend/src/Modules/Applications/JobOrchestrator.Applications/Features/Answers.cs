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
            try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, SavedAnswer>>(File.ReadAllText(FilePath), Json) ?? [] : []; }
            catch (Exception ex) when (ex is JsonException or IOException) { return []; }
        }
    }

    public static void Save(string question, string? answer, DateTime now)
    {
        lock (Gate)
        {
            var all = Load();
            var key = CustomAnswer.Key(question);
            if (string.IsNullOrWhiteSpace(answer)) all.Remove(key); else all[key] = new SavedAnswer(question.Trim(), answer.Trim(), now);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(all, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }
}

internal sealed class AnswerEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/applications/answers").WithTags("Applications");
        g.MapGet("", () => Results.Ok(AnswerStore.Load().Values.OrderByDescending(a => a.At)));
        g.MapPut("", (AnswerInput body, TimeProvider time) =>
        {
            if (string.IsNullOrWhiteSpace(body.Question) || body.Question.Length > 600) return Results.Problem("Pergunta vazia ou longa demais.", statusCode: 400);
            if (body.Answer is { Length: > 2000 }) return Results.Problem("Resposta longa demais.", statusCode: 400);
            AnswerStore.Save(body.Question, body.Answer, time.GetUtcNow().UtcDateTime);
            return Results.NoContent();
        });
    }
}
