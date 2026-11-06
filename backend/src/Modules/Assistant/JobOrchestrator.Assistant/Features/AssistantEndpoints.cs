namespace JobOrchestrator.Assistant.Features;

public sealed record OnboardingView(bool Done, bool AiReady, IReadOnlyList<string> Accounts, bool LocalReady, int Facts, bool ProfileConfirmed, string FirstName, bool Campaign, int Keywords, int Sources);
public sealed record OnboardingUpdate(bool Done);

/// <summary>Rotas do assistente (conversa, histórico, proposta do primeiro contato) e do estado do primeiro contato.</summary>
internal sealed class AssistantEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/assistant").WithTags("Assistant");
        g.MapPost("ask", async (AskRequest body, AssistantService assistant, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(body.Message) ? Results.BadRequest() : Results.Ok(await assistant.AskAsync(body, ct)));
        g.MapGet("conversations", async (AssistantService assistant, CancellationToken ct) => Results.Ok(await assistant.ConversationsAsync(ct)));
        g.MapGet("conversations/{id:guid}", async (Guid id, AssistantService assistant, CancellationToken ct) => Results.Ok(await assistant.ConversationAsync(id, ct)));
        g.MapPost("setup", async (AssistantService assistant, CancellationToken ct) => Results.Ok(await assistant.SetupAsync(ct)));
        g.MapPost("cover/{jobId:guid}", async (Guid jobId, CoverInput? body, AssistantService assistant, CancellationToken ct) =>
            Results.Ok(await assistant.CoverAsync(jobId, ct, body?.Tone, body?.Language)));

        app.MapGet("/api/onboarding", async (IConfiguration config, IIntelligenceApi ai, IProfileApi profile, ICampaignsApi campaigns, CancellationToken ct) =>
        {
            var providers = await ai.ProvidersAsync(ct);
            var p = await profile.CurrentAsync(ct);
            var real = p is { Synthetic: false };
            var c = (await campaigns.ListAsync(ct)).FirstOrDefault();
            return Results.Ok(new OnboardingView(config.GetValue("Onboarding:Done", false), providers.Any(x => x.Available),
                [.. providers.Where(x => x.Available).Select(x => x.Name)], providers.Any(x => x.Available && x.Family == "local"),
                real ? p!.Facts.Count : 0, real && p!.Confirmed, real ? p!.Identity.FirstName : "", c is not null, c?.Technologies.Count ?? 0, c?.Sources.Count ?? 0));
        }).WithTags("Assistant");
        app.MapPut("/api/onboarding", async (OnboardingUpdate body) =>
        {
            await UserSettings.SetAsync(new Dictionary<string, object?> { ["Onboarding:Done"] = body.Done });
            return Results.NoContent();
        }).WithTags("Assistant");
    }
}

/// <summary>O tom (direto, formal, caloroso) e o idioma (pt, en, es; vazio é o da vaga) da carta.</summary>
internal sealed record CoverInput(string? Tone, string? Language);
