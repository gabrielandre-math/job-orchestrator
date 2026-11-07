namespace JobOrchestrator.Automation.Features;

/// <summary>The platform matrix for the screen: what the app fills and sends by itself, and what stays with you.</summary>
internal sealed class PlatformEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/api/automation/platforms", (IAutomationApi automation) => Results.Ok(automation.Platforms())).WithTags("Automation");
}
