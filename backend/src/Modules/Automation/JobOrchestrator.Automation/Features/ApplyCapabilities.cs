using JobOrchestrator.Automation.Adapters;

namespace JobOrchestrator.Automation.Features;

/// <summary>What the app does with an application, from the form adapters it really has: no adapter means the copilot and you.</summary>
internal static class ApplyCapabilities
{
    public static ApplyCapability Of(IEnumerable<IFormAdapter> forms, string applyUrl, string channel)
    {
        if (channel == "email") return new("email", null, true, true, "smtp", "uncertain");
        if (channel != "ats" || !Uri.TryCreate(applyUrl, UriKind.Absolute, out var url)) return new("manual", null, false, false, "declared", "you");
        var form = forms.Where(f => f.Fit(url) > 0).OrderByDescending(f => f.Fit(url)).FirstOrDefault();
        return form is null ? new("manual", null, false, false, "declared", "you")
            : new("ats", form.Name, true, true, "receipt", form.CanReconcile(url) ? "portal" : "email");
    }

    public static IReadOnlyList<PlatformCapability> Matrix(IEnumerable<IFormAdapter> forms) =>
        [.. forms.Select(f => new PlatformCapability(f.Id, f.Name, f.LiveDomains, f.Capabilities.Contains(Capability.PrepareForm),
            f.Capabilities.Contains(Capability.SubmitApplication), true, f.LiveDomains.Any(d => f.CanReconcile(new Uri($"https://{d}")))))];
}
