using JobOrchestrator.Automation.Adapters;
using JobOrchestrator.Automation.Contracts;
using JobOrchestrator.Automation.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Each application says how it goes out: a real ATS form the app prepares, the job's own site, e-mail, or you alone.</summary>
public sealed class ApplyCapabilityTests
{
    private sealed class Form(string name, string live, string test) : IFormAdapter
    {
        public string Id => name.ToLowerInvariant();
        public string Version => "1";
        public string Name => $"Formulário {name}";
        public string Kind => "teste";
        public double Risk => 0.3;
        public IReadOnlyList<Capability> Capabilities => [Capability.PrepareForm];
        public IReadOnlyList<string> Domains => [live, test];
        public IReadOnlyList<string> LiveDomains => [live];
        public double Fit(Uri url) => url.Authority == live || url.Authority == test ? 1 : 0;
        public Task<AdapterResult<PreparedForm>> PrepareAsync(ApplyTarget target, MaterialBundle materials, CancellationToken ct) => throw new NotSupportedException();
    }

    private static readonly IFormAdapter[] Forms = [new Form("Greenhouse", "boards.greenhouse.io", "127.0.0.1:5470")];

    [Fact]
    public void A_real_ats_form_is_prepared_for_you_to_send() =>
        Assert.Equal(new ApplyCapability("ats", "Formulário Greenhouse", true), ApplyCapabilities.Of(Forms, "https://boards.greenhouse.io/acme/jobs/1", "ats"));

    [Fact]
    public void A_site_without_adapter_is_the_copilot_and_you() =>
        Assert.Equal(new ApplyCapability("manual", null, false), ApplyCapabilities.Of(Forms, "https://acme.gupy.io/jobs/1", "ats"));

    [Fact]
    public void An_email_job_is_prepared_as_an_email() =>
        Assert.Equal(new ApplyCapability("email", null, true), ApplyCapabilities.Of(Forms, "mailto:jobs@acme.example", "email"));

    [Fact]
    public void The_matrix_lists_live_sites_only()
    {
        var row = Assert.Single(ApplyCapabilities.Matrix(Forms));
        Assert.Equal(["boards.greenhouse.io"], row.Domains);
        Assert.True(row.Prepare);
    }
}
