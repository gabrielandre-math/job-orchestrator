using JobOrchestrator.Automation.Adapters;
using JobOrchestrator.Automation.Contracts;
using JobOrchestrator.Automation.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Each application says what the app will do with it: a real adapter fills and sends; anything else is the copilot and you.</summary>
public sealed class ApplyCapabilityTests
{
    private sealed class Form(string name, string live, string test) : IFormAdapter
    {
        public string Id => name.ToLowerInvariant();
        public string Version => "1";
        public string Name => $"Formulário {name}";
        public string Kind => "teste";
        public double Risk => 0.3;
        public IReadOnlyList<Capability> Capabilities => [Capability.PrepareForm, Capability.SubmitApplication, Capability.ReconcileSubmission];
        public IReadOnlyList<string> Domains => [live, test];
        public IReadOnlyList<string> LiveDomains => [live];
        public double Fit(Uri url) => url.Authority == live || url.Authority == test ? 1 : 0;
        public bool CanReconcile(Uri url) => url.Authority == test;
        public Task<AdapterResult<PreparedForm>> PrepareAsync(ApplyTarget target, MaterialBundle materials, CancellationToken ct) => throw new NotSupportedException();
        public Task<AdapterResult<SubmissionReceipt>> SubmitAsync(ApplyTarget target, MaterialBundle materials, SubmitToken token, CancellationToken ct) => throw new NotSupportedException();
        public Task<AdapterResult<ReconcileResult>> ReconcileAsync(ApplyTarget target, MaterialBundle materials, CancellationToken ct) => throw new NotSupportedException();
    }

    private static readonly IFormAdapter[] Forms = [new Form("Greenhouse", "boards.greenhouse.io", "127.0.0.1:5470")];

    [Fact]
    public void A_real_ats_is_filled_and_sent_and_you_check_the_confirmation_email() =>
        Assert.Equal(new ApplyCapability("ats", "Formulário Greenhouse", true, true, "receipt", "email"),
            ApplyCapabilities.Of(Forms, "https://boards.greenhouse.io/acme/jobs/1", "ats"));

    [Fact]
    public void The_test_ats_can_be_asked_after_a_cut() =>
        Assert.Equal("portal", ApplyCapabilities.Of(Forms, "http://127.0.0.1:5470/greenhouse/jobs/1", "ats").Recover);

    [Fact]
    public void A_site_without_adapter_is_the_copilot_and_you() =>
        Assert.Equal(new ApplyCapability("manual", null, false, false, "declared", "you"), ApplyCapabilities.Of(Forms, "https://acme.gupy.io/jobs/1", "ats"));

    [Fact]
    public void An_email_job_is_sent_by_your_account_and_waits_for_you_when_cut() =>
        Assert.Equal(("smtp", "uncertain"), (ApplyCapabilities.Of(Forms, "mailto:jobs@acme.example", "email").Confirm, ApplyCapabilities.Of(Forms, "", "email").Recover));

    [Fact]
    public void The_matrix_lists_live_sites_only_and_no_automatic_recovery_on_them()
    {
        var row = Assert.Single(ApplyCapabilities.Matrix(Forms));
        Assert.Equal(["boards.greenhouse.io"], row.Domains);
        Assert.False(row.Recover);
    }
}
