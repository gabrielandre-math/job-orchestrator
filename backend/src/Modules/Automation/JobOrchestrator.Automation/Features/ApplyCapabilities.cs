using JobOrchestrator.Automation.Adapters;

namespace JobOrchestrator.Automation.Features;

/// <summary>What the app does with an application, from the form adapters it really has: no adapter means the copilot and you.</summary>
internal static class ApplyCapabilities
{
    /// <summary>A link to a site without an adapter of its own is "site": the copilot fills what it finds there, the answer kit has the rest.</summary>
    public static ApplyCapability Of(IEnumerable<IFormAdapter> forms, string applyUrl, string channel)
    {
        if (channel == "email") return new("email", null, true);
        if (channel is not ("ats" or "link") || !Uri.TryCreate(applyUrl, UriKind.Absolute, out var url)) return new("manual", null, false);
        var form = forms.Where(f => f.Fit(url) > 0).OrderByDescending(f => f.Fit(url)).FirstOrDefault();
        if (form is null) return new("manual", null, false);
        return form is GenericForm ? new("site", form.Name, true) : new("ats", form.Name, true);
    }

    public static IReadOnlyList<PlatformCapability> Matrix(IEnumerable<IFormAdapter> forms) =>
        [.. forms.Select(f => new PlatformCapability(f.Id, f.Name, f.LiveDomains, f.Capabilities.Contains(Capability.PrepareForm)))];
}
