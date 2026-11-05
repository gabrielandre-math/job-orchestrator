namespace JobOrchestrator.Campaigns.Features;

internal sealed record GrantInput(int Days, int MaxPerDay, int MinScore, string[]? Adapters, bool Accepted);
internal sealed record AuthorizationItem(Guid Id, DateTime GrantedAt, DateTime ExpiresAt, int MaxPerDay, int MinScore, IReadOnlyList<string> Adapters, DateTime? RevokedAt, string Status);

/// <summary>
/// Autorizações de envio automático. Só nascem por esta rota, com o texto aceito na tela; o arquivo de configuração não liga o automático.
/// Conceder liga o modo AutoSubmit; revogar (ou vencer) devolve para a revisão sua.
/// </summary>
internal sealed class AuthorizationEndpoints : ICarterModule
{
    public const string Text = "Autorizo o Job Orchestrator a enviar candidaturas sem me perguntar a cada vez, só nesta campanha, só para vagas com a nota mínima abaixo, " +
        "só pelos adapters escolhidos, até o teto diário e até a data de validade. Cada envio passa pelo Guardião e fica registrado com evidência. Posso revogar a qualquer momento.";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/campaigns/{id:guid}/authorizations").WithTags("Campaigns");
        app.MapGet("/api/authorizations/text", () => Results.Ok(new { text = Text })).WithTags("Campaigns");
        g.MapGet("", async (Guid id, CampaignsDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var now = time.GetUtcNow().UtcDateTime;
            return Results.Ok((await db.Authorizations.AsNoTracking().Where(a => a.CampaignId == id).OrderByDescending(a => a.GrantedAt).Take(20).ToListAsync(ct))
                .Select(a => new AuthorizationItem(a.Id, a.GrantedAt, a.ExpiresAt, a.MaxPerDay, a.MinScore, a.Adapters, a.RevokedAt,
                    a.RevokedAt is not null ? "revogada" : a.ExpiresAt <= now ? "vencida" : "ativa")));
        });
        g.MapPost("", async (Guid id, GrantInput body, CampaignsDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var now = time.GetUtcNow().UtcDateTime;
            var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (campaign is null) return Results.NotFound();
            if (!body.Accepted) return Results.Problem("Marque o texto da autorização para continuar.", statusCode: 400);
            if (body.Days is < 1 or > 30) return Results.Problem("A validade vai de 1 a 30 dias.", statusCode: 400);
            if (body.MaxPerDay < 1 || body.MaxPerDay > Math.Min(15, campaign.MaxSubmissionsPerDay)) return Results.Problem($"O teto por dia vai de 1 a {Math.Min(15, campaign.MaxSubmissionsPerDay)}.", statusCode: 400);
            if (body.MinScore < campaign.MinFitScore || body.MinScore > 100) return Results.Problem($"A nota mínima não pode ficar abaixo do corte da campanha ({campaign.MinFitScore}).", statusCode: 400);
            foreach (var old in await db.Authorizations.Where(a => a.CampaignId == id && a.RevokedAt == null).ToListAsync(ct)) old.RevokedAt = now;
            var auth = new SubmitAuthorization
            {
                CampaignId = id, GrantedAt = now, ExpiresAt = now.AddDays(body.Days), MaxPerDay = body.MaxPerDay, MinScore = body.MinScore,
                Adapters = [.. (body.Adapters ?? []).Where(a => a.Length > 0).Distinct()], Text = Text,
            };
            db.Authorizations.Add(auth);
            campaign.EnableAuto(auth, now);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { auth.Id, auth.ExpiresAt });
        });
        g.MapDelete("{authId:guid}", async (Guid id, Guid authId, CampaignsDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var now = time.GetUtcNow().UtcDateTime;
            var auth = await db.Authorizations.FirstOrDefaultAsync(a => a.Id == authId && a.CampaignId == id, ct);
            if (auth is null) return Results.NotFound();
            auth.RevokedAt ??= now;
            if (await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct) is { } campaign) campaign.DisableAuto(now);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
