namespace JobOrchestrator.Operations.Features;

/// <summary>Rotas dos termos de uso: o painel pergunta na primeira vez e grava o aceite (Consent).</summary>
internal sealed class TermsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/terms").WithTags("Operations");
        g.MapGet("", () => Results.Ok(new { version = Consent.Version, accepted = Consent.Accepted, acceptedAt = Consent.Current()?.AcceptedAt }));
        g.MapPost("accept", () => Results.Ok(new { version = Consent.Version, accepted = true, acceptedAt = Consent.Accept().AcceptedAt }));
    }
}
